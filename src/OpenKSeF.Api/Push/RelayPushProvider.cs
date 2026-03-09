using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Push;

/// <summary>
/// Forwards push notifications to the team-operated relay service which
/// holds the Firebase/APNs credentials. Self-hosted admins don't need
/// to configure Firebase at all -- the relay handles delivery.
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

        try
        {
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Relay push sent for token {Token}", deviceToken[..Math.Min(8, deviceToken.Length)]);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Relay push failed: {Status} {Body}",
                response.StatusCode, body[..Math.Min(200, body.Length)]);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relay push request failed");
            return false;
        }
    }

    private static string ComputeHmac(string payload, string timestamp, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return "";

        var key = Encoding.UTF8.GetBytes(apiKey);
        var message = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
