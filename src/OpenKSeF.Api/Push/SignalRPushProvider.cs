using Microsoft.AspNetCore.SignalR;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.Push;

/// <summary>
/// Delivers push notifications to connected SignalR clients. Works only when
/// the mobile app has an active hub connection (foreground / background with
/// persistent connection). Falls through to the next provider otherwise.
/// </summary>
public class SignalRPushProvider : IUserPushProvider
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRPushProvider> _logger;

    public SignalRPushProvider(IHubContext<NotificationHub> hubContext, ILogger<SignalRPushProvider> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<bool> SendToUserAsync(string userId, PushNotification notification)
    {
        try
        {
            await _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", new
            {
                notification.Title,
                notification.Body,
                notification.Data
            });

            _logger.LogDebug("SignalR notification sent to user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for user {UserId}", userId);
            return false;
        }
    }
}
