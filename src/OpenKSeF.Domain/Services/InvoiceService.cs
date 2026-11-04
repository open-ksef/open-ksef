using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using System.Linq;

namespace OpenKSeF.Domain.Services;

public class InvoiceService : IInvoiceService
{
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

    public async Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices, bool notify)
    {
        var newInvoiceIds = new List<Guid>();
        var newInvoiceEvents = new List<NewInvoiceDetectedEvent>();
        var now = DateTime.UtcNow;

        foreach (var invoice in invoices)
        {
            var existing = await _db.InvoiceHeaders
                .FirstOrDefaultAsync(i =>
                    i.TenantId == tenantId &&
                    i.KSeFInvoiceNumber == invoice.Number);

            if (existing is null)
            {
                var newInvoice = new InvoiceHeader
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    KSeFInvoiceNumber = invoice.Number,
                    KSeFReferenceNumber = invoice.ReferenceNumber,
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
                    LastUpdatedAt = now
                };

                _db.InvoiceHeaders.Add(newInvoice);
                newInvoiceIds.Add(newInvoice.Id);
                newInvoiceEvents.Add(new NewInvoiceDetectedEvent(
                    tenantId, newInvoice.Id, invoice.VendorName,
                    invoice.InvoiceNumber, invoice.AmountGross,
                    invoice.Currency ?? "PLN"));

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
                existing.AcquisitionDate = invoice.AcquisitionDate;
                existing.InvoiceType = invoice.InvoiceType;
                existing.LastUpdatedAt = now;

                _logger.LogDebug(
                    "Updated existing invoice: {KSeFNumber} for tenant {TenantId}",
                    invoice.Number, tenantId);
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Upserted {Total} invoices for tenant {TenantId}, {NewCount} new",
            newInvoiceIds.Count + (invoices.Count() - newInvoiceIds.Count),
            tenantId, newInvoiceIds.Count);

        if (!notify)
            return newInvoiceIds;

        // Fire-and-forget notifications for new invoices
        foreach (var evt in newInvoiceEvents)
        {
            try
            {
                await _notificationService.NotifyNewInvoiceAsync(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send notification for invoice {InvoiceId}", evt.InvoiceId);
            }
        }

        return newInvoiceIds;
    }

    public async Task ApplyInvoiceXmlAsync(
        Guid invoiceId, string? bankAccount, IReadOnlyList<InvoiceLineDto> lines, DateOnly? dueDate, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.InvoiceHeaders.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return;

        if (invoice.Direction == InvoiceDirection.Received)
        {
            invoice.VendorBankAccount ??= bankAccount;
            invoice.DueDate ??= dueDate;
        }

        if (lines.Count > 0 && !await _db.InvoiceLines.AnyAsync(l => l.InvoiceHeaderId == invoiceId, cancellationToken))
            _db.InvoiceLines.AddRange(lines.Select(l => MapLine(l, invoiceId)));

        invoice.XmlFetchedAt = DateTime.UtcNow;
        invoice.LastUpdatedAt = invoice.XmlFetchedAt.Value;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailedInvoiceXmlAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.InvoiceHeaders.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return;

        invoice.XmlAttemptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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
