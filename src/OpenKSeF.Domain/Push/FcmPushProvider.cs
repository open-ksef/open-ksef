using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Push;

public class FcmPushProvider : IPushProvider
{
    private readonly ILogger<FcmPushProvider> _logger;

    public FcmPushProvider(ILogger<FcmPushProvider> logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendAsync(string deviceToken, PushNotification notification)
    {
        if (FirebaseApp.DefaultInstance is null)
        {
            _logger.LogWarning("Firebase is not initialized, skipping FCM push");
            return false;
        }

        var message = new Message
        {
            Token = deviceToken,
            Notification = new Notification
            {
                Title = notification.Title,
                Body = notification.Body
            },
            Data = notification.Data
        };

        try
        {
            var messageId = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            _logger.LogDebug("FCM message sent: {MessageId}", messageId);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            _logger.LogWarning("FCM token invalid or unregistered: {Token}", deviceToken[..8]);
            return false;
        }
    }
}
