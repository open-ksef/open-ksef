namespace OpenKSeF.Portal.Services;

public interface IInvoiceDetailService
{
    Task<InvoiceDetailModel?> GetByKsefNumberAsync(string ksefInvoiceNumber);
}
