using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Domain.Services;

public interface IInvoiceService
{
    /// <param name="notify">False stores new rows as already notified, so no push is ever owed for them.</param>
    Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices, bool notify = false);

    /// <remarks>REQ-0010 cap: the newest <see cref="InvoiceService.NotificationCap"/> by AcquisitionDate notify one by one, one summary covers the rest. Rows whose push settled get <c>NotifiedAt</c>.</remarks>
    Task NotifyNewInvoicesAsync(Guid tenantId, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default, bool email = true);

    /// <summary>Push-only retry of received rows still owing a push, first seen within <see cref="InvoiceService.PushRetryWindow"/>; older owed rows are stamped and never pushed.</summary>
    Task NotifyPendingAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <remarks>Never writes <c>IsPaid</c>/<c>PaidAt</c>; adds lines only when the invoice has none. Stores <paramref name="xml"/> as the downloadable original.</remarks>
    Task ApplyInvoiceXmlAsync(Guid invoiceId, InvoiceXmlDetails details, byte[]? xml = null, CancellationToken cancellationToken = default);

    Task RecordFailedInvoiceXmlAsync(Guid invoiceId, bool xmlReceived, byte[]? xml = null, CancellationToken cancellationToken = default);
}
