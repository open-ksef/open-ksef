using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Events;

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

    public async Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices)
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
                    AmountNet = invoice.AmountNet,
                    AmountVat = invoice.AmountVat,
                    AmountGross = invoice.AmountGross,
                    Currency = invoice.Currency ?? "PLN",
                    IssueDate = invoice.IssueDate,
                    AcquisitionDate = invoice.AcquisitionDate,
                    InvoiceType = invoice.InvoiceType,
                    VendorBankAccount = invoice.VendorBankAccount,
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
                existing.InvoiceNumber = invoice.InvoiceNumber;
                existing.VendorName = invoice.VendorName;
                existing.VendorNip = invoice.VendorNip;
                existing.BuyerName = invoice.BuyerName;
                existing.BuyerNip = invoice.BuyerNip;
                existing.AmountNet = invoice.AmountNet;
                existing.AmountVat = invoice.AmountVat;
                existing.AmountGross = invoice.AmountGross;
                existing.Currency = invoice.Currency ?? "PLN";
                existing.AcquisitionDate = invoice.AcquisitionDate;
                existing.InvoiceType = invoice.InvoiceType;
                if (invoice.VendorBankAccount is not null)
                    existing.VendorBankAccount = invoice.VendorBankAccount;
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
}
