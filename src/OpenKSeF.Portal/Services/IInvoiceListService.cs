using OpenKSeF.Domain.Models;

namespace OpenKSeF.Portal.Services;

public interface IInvoiceListService
{
    Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceListQuery query);
}
