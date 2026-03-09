using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Domain.Services;

public interface IInvoiceService
{
    Task<List<Guid>> UpsertInvoicesAsync(Guid tenantId, IEnumerable<InvoiceDto> invoices);
}
