using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using System.Linq;
using System.Security.Cryptography;

namespace OpenKSeF.Domain.Services;

public class InvoiceService : IInvoiceService
{
    public const int MaxFailedXmlReads = 5;
    public static readonly TimeSpan CappedXmlRetryAfter = TimeSpan.FromDays(7);
    public const int NotificationCap = 10;
    public static readonly TimeSpan PushRetryWindow = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        ApplicationDbContext db,
        INotificationService notificationService,
        ILogger<InvoiceService> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices, bool notify = false)
    {
        var newInvoiceIds = new List<Guid>();
        var now = DateTime.UtcNow;
        var page = invoices.DistinctBy(i => i.Number).ToList();
        var numbers = page.Select(i => i.Number).ToList();
        var existingByNumber = await _db.InvoiceHeaders
            .Where(i => i.TenantId == tenantId && numbers.Contains(i.KSeFInvoiceNumber))
            .ToDictionaryAsync(i => i.KSeFInvoiceNumber);

        foreach (var invoice in page)
        {
            if (!existingByNumber.TryGetValue(invoice.Number, out var existing))
            {
                var newInvoice = new InvoiceHeader
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    KSeFInvoiceNumber = invoice.Number,
                    KSeFReferenceNumber = invoice.Number,
                    InvoiceNumber = invoice.InvoiceNumber,
                    VendorName = invoice.VendorName,
                    VendorNip = invoice.VendorNip,
                    BuyerName = invoice.BuyerName,
                    BuyerNip = invoice.BuyerNip,
                    BuyerIdType = invoice.BuyerIdType,
                    BuyerForeignId = invoice.BuyerForeignId,
                    Direction = invoice.Direction,
                    AmountNet = invoice.AmountNet,
                    AmountVat = invoice.AmountVat,
                    AmountGross = invoice.AmountGross,
                    Currency = invoice.Currency ?? "PLN",
                    IssueDate = invoice.IssueDate,
                    AcquisitionDate = invoice.AcquisitionDate,
                    InvoiceType = invoice.InvoiceType,
                    FirstSeenAt = now,
                    LastUpdatedAt = now,
                    NotifiedAt = notify ? null : now
                };

                _db.InvoiceHeaders.Add(newInvoice);
                newInvoiceIds.Add(newInvoice.Id);

                _logger.LogInformation(
                    "New invoice detected: {KSeFNumber} ({InvoiceNumber}) from {Vendor} for tenant {TenantId}",
                    invoice.Number, invoice.InvoiceNumber, invoice.VendorName, tenantId);
            }
            else
            {
                if (existing.Direction != invoice.Direction)
                    continue;

                existing.InvoiceNumber = invoice.InvoiceNumber;
                existing.VendorName = invoice.VendorName;
                existing.VendorNip = invoice.VendorNip;
                existing.BuyerName = invoice.BuyerName;
                existing.BuyerNip = invoice.BuyerNip;
                existing.BuyerIdType = invoice.BuyerIdType;
                existing.BuyerForeignId = invoice.BuyerForeignId;
                existing.AmountNet = invoice.AmountNet;
                existing.AmountVat = invoice.AmountVat;
                existing.AmountGross = invoice.AmountGross;
                existing.Currency = invoice.Currency ?? "PLN";
                existing.IssueDate = invoice.IssueDate;
                existing.AcquisitionDate = invoice.AcquisitionDate;
                existing.InvoiceType = invoice.InvoiceType;
                existing.LastUpdatedAt = now;

                _logger.LogDebug(
                    "Updated existing invoice: {KSeFNumber} for tenant {TenantId}",
                    invoice.Number, tenantId);
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Invoice page not stored for tenant {TenantId}, KSeF numbers: {KSeFNumbers}",
                tenantId, string.Join(", ", page.Select(i => i.Number)));
            throw;
        }

        _logger.LogInformation(
            "Upserted {Total} invoices for tenant {TenantId}, {NewCount} new",
            page.Count, tenantId, newInvoiceIds.Count);

        return newInvoiceIds;
    }

    public async Task NotifyNewInvoicesAsync(Guid tenantId, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default, bool email = true)
    {
        if (invoiceIds.Count == 0)
            return;

        var notified = await _db.InvoiceHeaders
            .Where(i => invoiceIds.Contains(i.Id))
            .OrderByDescending(i => i.AcquisitionDate.HasValue)
            .ThenByDescending(i => i.AcquisitionDate)
            .Take(NotificationCap)
            .Select(i => new NewInvoiceDetectedEvent(i.TenantId, i.Id, i.VendorName, i.InvoiceNumber, i.AmountGross, i.Currency))
            .ToListAsync();

        var sent = 0;
        foreach (var evt in notified)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            sent++;
            try
            {
                if (await _notificationService.NotifyNewInvoiceAsync(evt, email))
                    await StampNotifiedAsync([evt.InvoiceId]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send notification for invoice {InvoiceId}", evt.InvoiceId);
            }
        }

        if (cancellationToken.IsCancellationRequested && invoiceIds.Count > sent)
        {
            _logger.LogWarning("Sync cancelled: notifications skipped for {Count} stored invoices, tenant {TenantId}", invoiceIds.Count - sent, tenantId);
            return;
        }

        var rest = invoiceIds.Except(notified.Select(e => e.InvoiceId)).ToList();
        if (rest.Count == 0)
            return;

        try
        {
            if (await _notificationService.NotifyNewInvoicesSummaryAsync(tenantId, rest.Count, email))
                await StampNotifiedAsync(rest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send summary notification for {Count} invoices, tenant {TenantId}", rest.Count, tenantId);
        }
    }

    public async Task NotifyPendingAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var since = now - PushRetryWindow;
        var expired = await _db.InvoiceHeaders
            .Where(i => i.TenantId == tenantId && i.NotifiedAt == null && i.FirstSeenAt < since)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.NotifiedAt, now), cancellationToken);
        if (expired > 0)
            _logger.LogWarning("Push retry window passed for {Count} invoices, tenant {TenantId}; not pushed", expired, tenantId);

        var pending = await _db.InvoiceHeaders
            .Where(i => i.TenantId == tenantId && i.Direction == InvoiceDirection.Received && i.NotifiedAt == null && i.FirstSeenAt >= since)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return;

        _logger.LogInformation("Retrying push for {Count} invoices, tenant {TenantId}", pending.Count, tenantId);
        await NotifyNewInvoicesAsync(tenantId, pending, cancellationToken, email: false);
    }

    // Bypasses the change tracker and sets only NotifiedAt: a stamp never flushes other pending edits or touches paid flags.
    private Task StampNotifiedAsync(IReadOnlyCollection<Guid> ids)
    {
        var now = DateTime.UtcNow;
        return _db.InvoiceHeaders
            .Where(i => ids.Contains(i.Id) && i.NotifiedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.NotifiedAt, now));
    }

    public async Task ApplyInvoiceXmlAsync(Guid invoiceId, InvoiceXmlDetails details, byte[]? xml = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.InvoiceHeaders.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return;

        if (invoice.Direction == InvoiceDirection.Received)
        {
            if (details.BankAccount is not null
                && (invoice.VendorBankAccount is null || QrCodeService.SanitizeAccount(details.BankAccount).Length > 0))
                invoice.VendorBankAccount = details.BankAccount;
            invoice.DueDate ??= details.DueDate;
            invoice.Settlement = details.Settlement;
            invoice.SettlementAmount = details.SettlementAmount;
            invoice.SplitPayment = details.SplitPayment;
            invoice.SellerPaidOn = details.SellerPaidOn;
            invoice.PartialPayment = details.PartialPayment;
            invoice.FactorBankAccount = details.FactorBankAccount;
            invoice.FactorNip = details.FactorNip;
            invoice.FactorName = details.FactorName;
        }

        var lines = details.Lines ?? [];
        if (lines.Count > 0 && !await _db.InvoiceLines.AnyAsync(l => l.InvoiceHeaderId == invoiceId, cancellationToken))
            _db.InvoiceLines.AddRange(lines.Select(l => MapLine(l, invoiceId)));

        invoice.XmlFetchedAt = DateTime.UtcNow;
        invoice.XmlFailedReads = null;
        invoice.LastUpdatedAt = invoice.XmlFetchedAt.Value;
        if (xml is not null)
            await StageXmlAsync(invoiceId, xml, invoice.XmlFetchedAt.Value, cancellationToken);
        if (invoice.Direction == InvoiceDirection.Received && details.SellerPaidOn is not null)
            await SaveDefaultingToSellerPaidAsync(invoiceId, invoice.XmlFetchedAt.Value, cancellationToken);
        else
            await _db.SaveChangesAsync(cancellationToken);
    }

    // REQ-0043: decided in SQL under the row lock, never from tracked values, so a concurrent apply or a user's paid toggle always wins.
    private async Task SaveDefaultingToSellerPaidAsync(Guid invoiceId, DateTime readAt, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "InvoiceHeaders" WHERE "Id" = {invoiceId} FOR UPDATE""", cancellationToken);
        var target = invoiceId.ToString();
        await _db.InvoiceHeaders
            .Where(i => i.Id == invoiceId && i.XmlFetchedAt == null && !i.IsPaid
                && !_db.AuditEvents.Any(a => a.TenantId == i.TenantId && a.Target == target
                    && (a.Action == AuditEvent.InvoicePaid || a.Action == AuditEvent.InvoiceUnpaid)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IsPaid, true)
                .SetProperty(i => i.PaidAt, readAt)
                .SetProperty(i => i.PaidByUserId, (string?)null), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordFailedInvoiceXmlAsync(Guid invoiceId, bool xmlReceived, byte[]? xml = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.InvoiceHeaders.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return;

        invoice.XmlAttemptedAt = DateTime.UtcNow;
        if (xmlReceived)
            invoice.XmlFailedReads = (short)((invoice.XmlFailedReads ?? 0) + 1);
        if (xml is not null)
            await StageXmlAsync(invoiceId, xml, invoice.XmlAttemptedAt.Value, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        if (xmlReceived && invoice.XmlFailedReads == MaxFailedXmlReads)
            _logger.LogWarning(
                "Invoice XML {KSeFNumber} failed to read {Count} times; incremental sync retries it after {RetryAfter}",
                invoice.KSeFInvoiceNumber, MaxFailedXmlReads, CappedXmlRetryAfter);
    }

    private async Task StageXmlAsync(Guid invoiceId, byte[] xml, DateTime now, CancellationToken cancellationToken)
    {
        var sha256 = Convert.ToBase64String(SHA256.HashData(xml));
        var stored = await _db.InvoiceXmls.FirstOrDefaultAsync(x => x.InvoiceHeaderId == invoiceId, cancellationToken);
        if (stored is null)
            _db.InvoiceXmls.Add(new InvoiceXml { InvoiceHeaderId = invoiceId, Content = xml, Sha256 = sha256, StoredAt = now });
        else if (stored.Sha256 != sha256)
        {
            stored.Content = xml;
            stored.Sha256 = sha256;
            stored.StoredAt = now;
        }
    }

    private static InvoiceLine MapLine(InvoiceLineDto dto, Guid invoiceHeaderId) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceHeaderId = invoiceHeaderId,
        LineNumber = dto.LineNumber,
        Name = dto.Name,
        Unit = dto.Unit,
        Quantity = dto.Quantity,
        UnitPriceNet = dto.UnitPriceNet,
        UnitPriceGross = dto.UnitPriceGross,
        AmountNet = dto.AmountNet,
        AmountGross = dto.AmountGross,
        AmountVat = dto.AmountVat,
        VatRate = dto.VatRate
    };
}
