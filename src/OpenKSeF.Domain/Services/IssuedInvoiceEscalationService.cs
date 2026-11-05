using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Flags invoices stuck in sending or missing UPO past the threshold: an Error log per window and one owner notification.
/// Never changes the invoice status.
/// </summary>
public sealed class IssuedInvoiceEscalationService(
    ApplicationDbContext db,
    INotificationService notifications,
    TimeProvider time,
    ILogger<IssuedInvoiceEscalationService> logger)
{
    private const int BatchSize = 50;

    public async Task EscalateAsync(TimeSpan threshold, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var cutoff = now - threshold;
        var stuck = await db.IssuedInvoices
            .Where(i => (i.Status == IssuedInvoiceStatus.Queued || i.Status == IssuedInvoiceStatus.Sending
                         || (i.Status == IssuedInvoiceStatus.Accepted && i.UpoXml == null))
                        && i.IssuedAt <= cutoff
                        && (i.EscalatedAt == null || i.EscalatedAt <= cutoff))
            .OrderBy(i => i.IssuedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var invoice in stuck)
        {
            var firstTime = invoice.EscalatedAt is null;
            invoice.MarkEscalated(now);
            try
            {
                // Saved before the push: a crash loses one push but cannot repeat it; the log and list flag still fire.
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.Entry(invoice).State = EntityState.Detached;
                continue;
            }

            logger.LogError(
                "Issued invoice {InvoiceId} of tenant {TenantId} needs attention: status {Status}, number {Number}, issued {IssuedAt}, attempts {AttemptCount}, session {SessionRef}, element {ElementRef}, last error {LastError}",
                invoice.Id, invoice.TenantId, invoice.Status, invoice.Number, invoice.IssuedAt, invoice.AttemptCount, invoice.KsefSessionRef, invoice.KsefElementRef, invoice.LastError);

            if (firstTime)
                await notifications.NotifyIssuedInvoiceStuckAsync(invoice.TenantId, invoice.Id, invoice.Number);
        }
    }
}
