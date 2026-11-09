using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Domain.Services;

public interface IInvoiceService
{
    /// <param name="notify">False for initial sync / full resync so history does not flood users with pushes.</param>
    Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices, bool notify);

    /// <remarks>Never writes <c>IsPaid</c>/<c>PaidAt</c>; adds lines only when the invoice has none.</remarks>
    Task ApplyInvoiceXmlAsync(Guid invoiceId, InvoiceXmlDetails details, CancellationToken cancellationToken = default);

    Task RecordFailedInvoiceXmlAsync(Guid invoiceId, bool xmlReceived, CancellationToken cancellationToken = default);
}
