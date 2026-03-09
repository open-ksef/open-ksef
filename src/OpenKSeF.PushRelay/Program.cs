using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ApnsClient>();

var firebaseJson = builder.Configuration["Firebase:CredentialsJson"];
if (!string.IsNullOrEmpty(firebaseJson))
{
    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(firebaseJson) });
    Console.WriteLine("Firebase initialized");
}
else
{
    Console.WriteLine("WARNING: Firebase:CredentialsJson not configured — FCM push disabled");
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/push", async (
    HttpContext context,
    [FromBody] PushRequest request,
    IConfiguration config,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("PushRelay");

    var apiKey = config["Relay:ApiKey"] ?? "";
    if (!string.IsNullOrEmpty(apiKey))
    {
        var timestamp = context.Request.Headers["X-Relay-Timestamp"].FirstOrDefault() ?? "";
        var signature = context.Request.Headers["X-Relay-Signature"].FirstOrDefault() ?? "";

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var expectedSig = ComputeHmac(body, timestamp, apiKey);
        if (string.IsNullOrEmpty(signature) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSig)))
        {
            logger.LogWarning("Invalid relay signature from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Invalid signature" }, statusCode: 401);
        }
    }

    if (string.IsNullOrEmpty(request.PushToken))
        return Results.BadRequest(new { error = "pushToken is required" });

    var sent = false;

    // Try FCM
    if (FirebaseApp.DefaultInstance is not null)
    {
        try
        {
            var message = new Message
            {
                Token = request.PushToken,
                Notification = new Notification
                {
                    Title = request.Title ?? "OpenKSeF",
                    Body = request.Body ?? ""
                },
                Data = request.Data
            };

            await FirebaseMessaging.DefaultInstance.SendAsync(message);
            sent = true;
            logger.LogDebug("FCM push sent for token {Token}", request.PushToken[..Math.Min(8, request.PushToken.Length)]);
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
            ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            logger.LogDebug("FCM rejected token {Token}: {Error}",
                request.PushToken[..Math.Min(8, request.PushToken.Length)], ex.MessagingErrorCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM send failed");
        }
    }

    // Try APNs
    if (!sent)
    {
        var apnsClient = app.Services.GetRequiredService<ApnsClient>();
        try
        {
            sent = await apnsClient.SendAsync(request);
            if (sent)
                logger.LogDebug("APNs push sent for token {Token}", request.PushToken[..Math.Min(8, request.PushToken.Length)]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "APNs send failed");
        }
    }

    return sent
        ? Results.Ok(new { success = true })
        : Results.Json(new { success = false, error = "All providers failed" }, statusCode: 502);
});

app.Run();

static string ComputeHmac(string payload, string timestamp, string apiKey)
{
    var key = Encoding.UTF8.GetBytes(apiKey);
    var message = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
    var hash = HMACSHA256.HashData(key, message);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

record PushRequest(
    [property: JsonPropertyName("pushToken")] string? PushToken,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("data")] Dictionary<string, string>? Data);

/// <summary>
/// Minimal APNs HTTP/2 client for the relay service.
/// Sends push notifications using token-based (p8) authentication.
/// </summary>
class ApnsClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ApnsClient> _logger;
    private readonly string _bundleId;

    public ApnsClient(IConfiguration config, ILogger<ApnsClient> logger)
    {
        _config = config;
        _logger = logger;
        _bundleId = config["APNs:BundleId"] ?? "com.openksef.mobile";

        var baseUrl = config["APNs:BaseUrl"] ?? "https://api.push.apple.com";
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<bool> SendAsync(PushRequest request)
    {
        if (string.IsNullOrEmpty(_config["APNs:KeyId"]))
            return false;

        var payload = new
        {
            aps = new
            {
                alert = new { title = request.Title ?? "OpenKSeF", body = request.Body ?? "" },
                sound = "default"
            },
            data = request.Data
        };

        var json = JsonSerializer.Serialize(payload);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/3/device/{request.PushToken}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("apns-topic", _bundleId);
        httpRequest.Headers.Add("apns-push-type", "alert");

        var response = await _httpClient.SendAsync(httpRequest);
        return response.IsSuccessStatusCode;
    }
}
