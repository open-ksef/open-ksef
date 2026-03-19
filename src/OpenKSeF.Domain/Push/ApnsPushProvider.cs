using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Push;

public class ApnsPushProvider : IPushProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApnsPushProvider> _logger;
    private readonly string _bundleId;

    public ApnsPushProvider(HttpClient httpClient, IConfiguration configuration, ILogger<ApnsPushProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _bundleId = configuration["APNs:BundleId"] ?? "com.openksef.mobile";
    }

    public async Task<bool> SendAsync(string deviceToken, PushNotification notification)
    {
        var payload = new
        {
            aps = new
            {
                alert = new { title = notification.Title, body = notification.Body },
                sound = "default"
            },
            data = notification.Data
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/3/device/{deviceToken}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apns-topic", _bundleId);
        request.Headers.Add("apns-push-type", "alert");

        try
        {
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("APNs push sent to {Token}", deviceToken[..8]);
                return true;
            }

            if ((int)response.StatusCode == 410)
            {
                _logger.LogWarning("APNs token expired: {Token}", deviceToken[..8]);
                return false;
            }

            _logger.LogWarning("APNs push failed with status {Status}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "APNs push failed for {Token}", deviceToken[..8]);
            return false;
        }
    }
}
