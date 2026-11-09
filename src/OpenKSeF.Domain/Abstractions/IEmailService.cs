using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface IEmailService
{
    Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt);
    Task SendNewInvoicesSummaryEmailAsync(string toEmail, Guid tenantId, string company, int count);
}
