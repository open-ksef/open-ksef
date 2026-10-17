using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface INotificationService
{
    Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt);
    Task SendConfirmationAsync(string deviceToken, Platform platform);
    Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform);
}
