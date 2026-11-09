using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Domain.Services;

public interface IInvoiceService
{
    Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices);

    /// <remarks>REQ-0010 cap: the newest <see cref="InvoiceService.NotificationCap"/> by AcquisitionDate notify one by one, one summary covers the rest.</remarks>
    Task NotifyNewInvoicesAsync(Guid tenantId, IReadOnlyCollection<Guid> invoiceIds);

    /// <remarks>Never writes <c>IsPaid</c>/<c>PaidAt</c>; adds lines only when the invoice has none.</remarks>
    Task ApplyInvoiceXmlAsync(Guid invoiceId, InvoiceXmlDetails details, CancellationToken cancellationToken = default);

    Task RecordFailedInvoiceXmlAsync(Guid invoiceId, bool xmlReceived, CancellationToken cancellationToken = default);
}
