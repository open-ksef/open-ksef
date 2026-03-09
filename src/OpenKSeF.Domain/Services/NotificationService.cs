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

        // Layer 1: Try SignalR (local push) per-user first
        var signalRUsers = new HashSet<string>();
        if (_userPushProvider is not null)
        {
            var userIds = deviceTokens.Select(d => d.UserId).Distinct();
            foreach (var userId in userIds)
            {
                try
                {
                    await _userPushProvider.SendToUserAsync(userId, notification);
                    signalRUsers.Add(userId);
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
            var success = await SendToSingleDeviceAsync(device.Token, notification);

            if (!success && !signalRUsers.Contains(device.UserId))
            {
                _logger.LogWarning(
                    "All push providers failed for device {DeviceId}, marking for removal",
                    device.Id);
                tokensToRemove.Add(device);
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

    public async Task SendConfirmationAsync(string deviceToken)
    {
        var notification = new PushNotification
        {
            Title = "Powiadomienia włączone",
            Body = "Tym kanałem będziesz otrzymywać powiadomienia o nowych fakturach.",
            Data = new Dictionary<string, string> { ["type"] = "confirmation" }
        };

        try
        {
            await SendToSingleDeviceAsync(deviceToken, notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Confirmation push failed for token {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
        }
    }

    public async Task<bool> SendTestNotificationAsync(string deviceToken)
    {
        var notification = new PushNotification
        {
            Title = "Test powiadomień",
            Body = "Powiadomienie testowe z OpenKSeF — połączenie działa poprawnie!",
            Data = new Dictionary<string, string> { ["type"] = "test" }
        };

        return await SendToSingleDeviceAsync(deviceToken, notification);
    }

    /// <summary>
    /// Per-token delivery: Relay > Direct FCM > APNs (in registration order).
    /// </summary>
    private async Task<bool> SendToSingleDeviceAsync(string token, PushNotification notification)
    {
        foreach (var provider in _pushProviders)
        {
            try
            {
                var success = await provider.SendAsync(token, notification);
                if (success) return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push provider failed for token {Token}",
                    token[..Math.Min(8, token.Length)]);
            }
        }

        return false;
    }
}
