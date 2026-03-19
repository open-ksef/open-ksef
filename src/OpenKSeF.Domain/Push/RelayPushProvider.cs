using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Push;

/// <summary>
/// Forwards push notifications to the team-operated relay service which
/// holds the Firebase/APNs credentials. Self-hosted admins don't need
/// to configure Firebase at all -- the relay handles delivery.
///
/// Authentication: each instance registers with the relay during setup
/// and receives a unique HMAC key. The key and instance ID are stored
/// in SystemConfig and sent with every request.
/// </summary>
public class RelayPushProvider : IPushProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISystemConfigService _systemConfig;
    private readonly ILogger<RelayPushProvider> _logger;

    public RelayPushProvider(
        IHttpClientFactory httpClientFactory,
        ISystemConfigService systemConfig,
        ILogger<RelayPushProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _systemConfig = systemConfig;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string deviceToken, PushNotification notification)
    {
        var relayUrl = _systemConfig.GetValue(SystemConfigKeys.PushRelayUrl);
        if (string.IsNullOrEmpty(relayUrl))
        {
            _logger.LogDebug("Push relay URL not configured, skipping relay delivery");
            return false;
        }

        var apiKey = _systemConfig.GetValue(SystemConfigKeys.PushRelayApiKey) ?? "";
        var instanceId = _systemConfig.GetValue(SystemConfigKeys.PushRelayInstanceId) ?? "";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Push relay API key not configured -- instance may not be registered. Skipping relay delivery");
            return false;
        }

        var payload = new
        {
            pushToken = deviceToken,
            title = notification.Title,
            body = notification.Body,
            data = notification.Data
        };

        var json = JsonSerializer.Serialize(payload);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeHmac(json, timestamp, apiKey);

        var client = _httpClientFactory.CreateClient("push-relay");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{relayUrl.TrimEnd('/')}/api/push")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Relay-Timestamp", timestamp);
        request.Headers.Add("X-Relay-Signature", signature);

        if (!string.IsNullOrEmpty(instanceId))
            request.Headers.Add("X-Relay-Instance", instanceId);

        try
        {
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Relay push sent for token {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;

            if (statusCode == 429 || statusCode >= 500)
            {
                throw new HttpRequestException(
                    $"Relay transient failure: {response.StatusCode} {body[..Math.Min(200, body.Length)]}",
                    null, response.StatusCode);
            }

            _logger.LogWarning("Relay push failed: {Status} {Body}",
                response.StatusCode, body[..Math.Min(200, body.Length)]);
            return false;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relay push request failed");
            return false;
        }
    }

    private static string ComputeHmac(string payload, string timestamp, string apiKey)
    {
        var key = Encoding.UTF8.GetBytes(apiKey);
        var message = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
