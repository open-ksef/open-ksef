using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<IPushProvider> _pushProviders;
    private readonly IUserPushProvider? _userPushProvider;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext db,
        IEnumerable<IPushProvider> pushProviders,
        IEmailService emailService,
        ILogger<NotificationService> logger,
        IUserPushProvider? userPushProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _pushProviders = pushProviders;
        _emailService = emailService;
        _logger = logger;
        _userPushProvider = userPushProvider;
    }

    public async Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt)
    {
        // TenantId == null means "all companies"; a set TenantId is an explicit per-company subscription.
        var deviceTokens = await _db.DeviceTokens
            .Where(d => d.TenantId == evt.TenantId || d.TenantId == null)
            .Where(d => _db.Tenants.Any(t =>
                t.Id == evt.TenantId && t.UserId == d.UserId))
            .ToListAsync();

        if (deviceTokens.Count == 0)
        {
            _logger.LogDebug("No device tokens for tenant {TenantId}", evt.TenantId);
            return;
        }

        var notification = new PushNotification
        {
            Title = "New invoice received",
            Body = $"New invoice from '{evt.VendorName}' — Amount: {evt.Amount:N2} {evt.Currency}",
            Data = new Dictionary<string, string>
            {
                ["tenantId"] = evt.TenantId.ToString(),
                ["invoiceId"] = evt.InvoiceId.ToString()
            }
        };

        // Layer 1: SignalR (in-app, API process only). Says nothing about remote delivery.
        if (_userPushProvider is not null)
        {
            var userIds = deviceTokens.Select(d => d.UserId).Distinct();
            foreach (var userId in userIds)
            {
                try
                {
                    await _userPushProvider.SendToUserAsync(userId, notification);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR push failed for user {UserId}", userId);
                }
            }
        }

        // Layer 2+3: Relay / Direct FCM / APNs per-token (always attempt for remote delivery)
        var tokensToRemove = new List<Entities.DeviceToken>();

        foreach (var device in deviceTokens)
        {
            var result = await SendToSingleDeviceAsync(device.Token, device.Platform, notification);

            if (result == PushResult.InvalidToken)
            {
                _logger.LogWarning(
                    "Push token rejected by FCM/APNs for device {DeviceId}, marking for removal",
                    device.Id);
                tokensToRemove.Add(device);
            }
            else if (result == PushResult.Failed)
            {
                _logger.LogWarning(
                    "Push not delivered to device {DeviceId} for invoice {InvoiceId}; token kept",
                    device.Id, evt.InvoiceId);
            }
        }

        if (tokensToRemove.Count > 0)
        {
            _db.DeviceTokens.RemoveRange(tokensToRemove);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Removed {Count} invalid device tokens", tokensToRemove.Count);
        }

        // Layer 4: Email fallback
        var tenant = await _db.Tenants
            .Where(t => t.Id == evt.TenantId)
            .Select(t => new { t.NotificationEmail })
            .FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(tenant?.NotificationEmail))
        {
            try
            {
                await _emailService.SendNewInvoiceEmailAsync(tenant.NotificationEmail, evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email fallback failed for tenant {TenantId}", evt.TenantId);
            }
        }
    }

    public async Task SendConfirmationAsync(string deviceToken, Platform platform)
    {
        var notification = new PushNotification
        {
            Title = "Powiadomienia włączone",
            Body = "Tym kanałem będziesz otrzymywać powiadomienia o nowych fakturach.",
            Data = new Dictionary<string, string> { ["type"] = "confirmation" }
        };

        try
        {
            await SendToSingleDeviceAsync(deviceToken, platform, notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Confirmation push failed for token {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
        }
    }

    public async Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform)
    {
        var notification = new PushNotification
        {
            Title = "Test powiadomień",
            Body = "Powiadomienie testowe z OpenKSeF — połączenie działa poprawnie!",
            Data = new Dictionary<string, string> { ["type"] = "test" }
        };

        return await SendToSingleDeviceAsync(deviceToken, platform, notification) == PushResult.Sent;
    }

    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = [500, 1000, 2000];

    /// <summary>
    /// Per-token delivery: Relay > Direct FCM > APNs (in registration order).
    /// Each provider is retried up to 3 times on transient (network) failures
    /// before falling through to the next provider.
    /// InvalidToken is returned only when a provider rejected the token and none failed transiently,
    /// so an outage never deletes a working token.
    /// </summary>
    private async Task<PushResult> SendToSingleDeviceAsync(string token, Platform platform, PushNotification notification)
    {
        // SignalR-only fallback ids have no FCM/APNs registration to send to.
        if (IsSignalRFallbackToken(token))
            return PushResult.Skipped;

        var shortToken = token[..Math.Min(8, token.Length)];
        var failed = false;
        var rejected = false;

        foreach (var provider in _pushProviders)
        {
            var result = PushResult.Failed;
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    result = await provider.SendAsync(token, platform, notification);
                    break;
                }
                catch (HttpRequestException ex) when (attempt < MaxRetries - 1)
                {
                    _logger.LogDebug(ex,
                        "Transient push failure for token {Token}, attempt {Attempt}/{Max}",
                        shortToken, attempt + 1, MaxRetries);
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt < MaxRetries - 1)
                {
                    _logger.LogDebug(ex,
                        "Push timeout for token {Token}, attempt {Attempt}/{Max}",
                        shortToken, attempt + 1, MaxRetries);
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push provider failed for token {Token}", shortToken);
                    break;
                }
            }

            if (result == PushResult.Sent) return PushResult.Sent;
            failed |= result == PushResult.Failed;
            rejected |= result == PushResult.InvalidToken;
        }

        return failed ? PushResult.Failed
            : rejected ? PushResult.InvalidToken
            : PushResult.Skipped;
    }

    private static bool IsSignalRFallbackToken(string token) =>
        token.StartsWith("device-", StringComparison.OrdinalIgnoreCase);
}
