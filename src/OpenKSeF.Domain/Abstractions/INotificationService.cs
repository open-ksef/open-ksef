using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface INotificationService
{
    Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt);
    Task SendConfirmationAsync(string deviceToken);
    Task<bool> SendTestNotificationAsync(string deviceToken);
}
