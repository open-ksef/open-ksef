using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface IEmailService
{
    Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt);
}
