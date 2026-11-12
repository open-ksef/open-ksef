using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Push;

public class FcmPushProvider : IPushProvider
{
    private static readonly object InitLock = new();
    private static string? _rejectedCredentials;

    private readonly ISystemConfigService _systemConfig;
    private readonly ILogger<FcmPushProvider> _logger;

    public FcmPushProvider(ISystemConfigService systemConfig, ILogger<FcmPushProvider> logger)
    {
        _systemConfig = systemConfig;
        _logger = logger;
    }

    public async Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
    {
        if (platform != Platform.Android)
            return PushResult.Skipped;

        if (FirebaseApp.DefaultInstance is null && !TryInitializeFirebase())
        {
            _logger.LogDebug("Firebase is not initialized, skipping direct FCM push");
            return PushResult.Skipped;
        }

        var message = new Message
        {
#pragma warning disable CS0618
            Token = deviceToken,
#pragma warning restore CS0618
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
        catch (FirebaseMessagingException ex)
        {
            var result = Classify(ex.MessagingErrorCode, ex.Message);
            if (result == PushResult.InvalidToken)
                _logger.LogWarning("FCM token invalid or unregistered: {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
            else
                _logger.LogWarning(ex, "FCM push failed: {ErrorCode} {Error}", ex.MessagingErrorCode, ex.Message);
            return result;
        }
    }

    /// <summary>Only an error naming the token deletes it; InvalidArgument also covers payload errors, which must keep the token.</summary>
    internal static PushResult Classify(MessagingErrorCode? code, string? message) => code switch
    {
        MessagingErrorCode.Unregistered => PushResult.InvalidToken,
        MessagingErrorCode.InvalidArgument when message?.Contains("registration token", StringComparison.OrdinalIgnoreCase) == true => PushResult.InvalidToken,
        _ => PushResult.Failed,
    };

    internal bool TryInitializeFirebase()
    {
        var credentialsJson = _systemConfig.GetValue(SystemConfigKeys.FirebaseCredentialsJson);
        if (string.IsNullOrEmpty(credentialsJson) || credentialsJson == _rejectedCredentials)
            return false;

        lock (InitLock)
        {
            if (FirebaseApp.DefaultInstance is not null)
                return true;

            try
            {
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(credentialsJson) });
                _logger.LogInformation("Firebase initialized");
                return true;
            }
            catch (Exception ex)
            {
                _rejectedCredentials = credentialsJson;
                _logger.LogWarning("Firebase credentials cannot be used, skipping direct FCM push: {Error}", ex.GetType().Name);
                return false;
            }
        }
    }
}
