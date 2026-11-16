using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface INotificationService
{
    /// <returns>False when no device got the push and one failed, so it should be retried; <paramref name="email"/> false skips e-mail.</returns>
    Task<bool> NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt, bool email = true);
    Task<bool> NotifyNewInvoicesSummaryAsync(Guid tenantId, int count, bool email = true);
    Task NotifyIssuedInvoiceStuckAsync(Guid tenantId, Guid issuedInvoiceId, string? number);
    Task NotifyAccessGrantedAsync(string userId, Guid tenantId, string tenantName);
    Task SendConfirmationAsync(string deviceToken, Platform platform);
    Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform);
}
