using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Push;

/// <summary>Forwards pushes to the team-operated relay, which holds the FCM/APNs credentials; each request is signed with this instance's HMAC key from SystemConfig.</summary>
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

    public async Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
    {
        var relayUrl = _systemConfig.GetValue(SystemConfigKeys.PushRelayUrl);
        if (string.IsNullOrEmpty(relayUrl))
        {
            _logger.LogDebug("Push relay URL not configured, skipping relay delivery");
            return PushResult.Skipped;
        }

        var apiKey = _systemConfig.GetValue(SystemConfigKeys.PushRelayApiKey) ?? "";
        var instanceId = _systemConfig.GetValue(SystemConfigKeys.PushRelayInstanceId) ?? "";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Push relay API key not configured -- instance may not be registered. Skipping relay delivery");
            return PushResult.Skipped;
        }

        var payload = new
        {
            pushToken = deviceToken,
            platform = platform == Platform.iOS ? "ios" : "android",
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
                return PushResult.Sent;
            }

            var body = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;

            // Relay answers 410 only when FCM/APNs rejected the token itself.
            if (statusCode == 410)
                return PushResult.InvalidToken;

            if (statusCode == 429 || statusCode >= 500)
            {
                throw new HttpRequestException(
                    $"Relay transient failure: {response.StatusCode} {body[..Math.Min(200, body.Length)]}",
                    null, response.StatusCode);
            }

            _logger.LogWarning("Relay push failed: {Status} {Body}",
                response.StatusCode, body[..Math.Min(200, body.Length)]);
            return PushResult.Failed;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relay push request failed");
            return PushResult.Failed;
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
