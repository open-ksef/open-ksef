using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Domain.Services;

public interface IInvoiceService
{
    /// <param name="notify">False for initial sync / full resync so history does not flood users with pushes.</param>
    Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices, bool notify);
}
