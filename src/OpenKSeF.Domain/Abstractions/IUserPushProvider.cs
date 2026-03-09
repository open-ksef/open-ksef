using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Abstractions;

/// <summary>
/// Push provider that delivers notifications by user ID rather than device token.
/// Used for SignalR local push where the server knows the connected user.
/// </summary>
public interface IUserPushProvider
{
    Task<bool> SendToUserAsync(string userId, PushNotification notification);
}
