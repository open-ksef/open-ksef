using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Push;

public class FcmPushProvider : IPushProvider
{
    private readonly ILogger<FcmPushProvider> _logger;

    public FcmPushProvider(ILogger<FcmPushProvider> logger)
    {
        _logger = logger;
    }

    public async Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
    {
        if (platform != Platform.Android)
            return PushResult.Skipped;

        if (FirebaseApp.DefaultInstance is null)
        {
            _logger.LogDebug("Firebase is not initialized, skipping direct FCM push");
            return PushResult.Skipped;
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
            return PushResult.Sent;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            _logger.LogWarning("FCM token invalid or unregistered: {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
            return PushResult.InvalidToken;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning(ex, "FCM push failed: {ErrorCode}", ex.MessagingErrorCode);
            return PushResult.Failed;
        }
    }
}
