using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenKSeF.PushRelay;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<InstanceStore>();
builder.Services.AddSingleton<ApnsClient>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    options.AddPolicy("push", context =>
    {
        var instanceId = context.Request.Headers["X-Relay-Instance"].FirstOrDefault() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(instanceId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy("register", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy("admin", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2,
            QueueLimit = 0,
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetSlidingWindowLimiter("global", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        }));
});

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

// Force InstanceStore initialization (creates DB file + schema)
_ = app.Services.GetRequiredService<InstanceStore>();

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// --- Registration endpoint ---
app.MapPost("/api/register", async (
    [FromBody] RegisterRequest req,
    InstanceStore store,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("PushRelay");

    if (string.IsNullOrWhiteSpace(req.InstanceUrl))
        return Results.BadRequest(new { error = "instanceUrl is required" });

    var (instanceId, apiKey) = await store.RegisterAsync(req.InstanceUrl);
    logger.LogInformation("New instance registered: {InstanceId} from {Url}", instanceId, req.InstanceUrl);

    return Results.Ok(new { instanceId, apiKey });
}).RequireRateLimiting("register");

// --- Push endpoint (secured) ---
app.MapPost("/api/push", async (
    HttpContext context,
    IConfiguration config,
    InstanceStore store,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("PushRelay");
    var instanceId = context.Request.Headers["X-Relay-Instance"].FirstOrDefault() ?? "";
    var timestamp = context.Request.Headers["X-Relay-Timestamp"].FirstOrDefault() ?? "";
    var signature = context.Request.Headers["X-Relay-Signature"].FirstOrDefault() ?? "";

    // Read raw body first (before any deserialization) for HMAC verification
    context.Request.Body.Position = 0;
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

    var request = JsonSerializer.Deserialize<PushRequest>(body);
    if (request == null)
        return Results.BadRequest(new { error = "Invalid request body" });

    // Timestamp freshness check (5 minute window)
    if (!long.TryParse(timestamp, out var ts))
    {
        logger.LogWarning("Missing or invalid timestamp from {RemoteIp}", context.Connection.RemoteIpAddress);
        return Results.Json(new { error = "Invalid timestamp" }, statusCode: 401);
    }

    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    if (Math.Abs(now - ts) > 300)
    {
        logger.LogWarning("Stale timestamp from {RemoteIp} (delta={Delta}s)", context.Connection.RemoteIpAddress, now - ts);
        return Results.Json(new { error = "Request expired" }, statusCode: 401);
    }

    // Look up the instance
    string apiKey;
    if (!string.IsNullOrEmpty(instanceId))
    {
        var instance = await store.GetAsync(instanceId);
        if (instance == null)
        {
            logger.LogWarning("Unknown instance {InstanceId} from {RemoteIp}", instanceId, context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Unknown instance" }, statusCode: 401);
        }

        if (!instance.Enabled)
        {
            logger.LogWarning("Disabled instance {InstanceId} from {RemoteIp}", instanceId, context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Instance disabled" }, statusCode: 403);
        }

        var expectedSig = ComputeHmac(body, timestamp, instance.ApiKey);
        if (string.IsNullOrEmpty(signature) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSig)))
        {
            logger.LogWarning("Invalid relay signature from instance {InstanceId} at {RemoteIp}", instanceId, context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Invalid signature" }, statusCode: 401);
        }

        _ = store.UpdateLastSeenAsync(instanceId);
    }
    else
    {
        // Legacy path: support global Relay:ApiKey for backward compatibility
        apiKey = config["Relay:ApiKey"] ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("No instance header and no global API key configured, rejecting from {RemoteIp}", context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Authentication required" }, statusCode: 401);
        }

        var expectedSig = ComputeHmac(body, timestamp, apiKey);
        if (string.IsNullOrEmpty(signature) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSig)))
        {
            logger.LogWarning("Invalid relay signature from {RemoteIp} (legacy mode)", context.Connection.RemoteIpAddress);
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
}).RequireRateLimiting("push");

// --- Admin endpoints (protected by Relay:AdminKey) ---
var adminGroup = app.MapGroup("/api/admin").RequireRateLimiting("admin");

adminGroup.AddEndpointFilter(async (context, next) =>
{
    var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
    var adminKey = config["Relay:AdminKey"] ?? "";
    if (string.IsNullOrEmpty(adminKey))
        return Results.Json(new { error = "Admin endpoints not configured" }, statusCode: 404);

    var provided = context.HttpContext.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? "";
    if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(adminKey)))
        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

    return await next(context);
});

adminGroup.MapGet("/instances", async (InstanceStore store) =>
{
    var instances = await store.ListAsync();
    return Results.Ok(instances.Select(i => new
    {
        i.InstanceId,
        i.InstanceUrl,
        i.RegisteredAt,
        i.LastSeenAt,
        i.Enabled,
    }));
});

adminGroup.MapPost("/instances/{instanceId}/disable", async (string instanceId, InstanceStore store) =>
{
    var instance = await store.GetAsync(instanceId);
    if (instance == null)
        return Results.NotFound(new { error = "Instance not found" });

    await store.SetEnabledAsync(instanceId, false);
    return Results.Ok(new { success = true });
});

adminGroup.MapPost("/instances/{instanceId}/enable", async (string instanceId, InstanceStore store) =>
{
    var instance = await store.GetAsync(instanceId);
    if (instance == null)
        return Results.NotFound(new { error = "Instance not found" });

    await store.SetEnabledAsync(instanceId, true);
    return Results.Ok(new { success = true });
});

app.Run();

static string ComputeHmac(string payload, string timestamp, string apiKey)
{
    var key = Encoding.UTF8.GetBytes(apiKey);
    var message = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
    var hash = HMACSHA256.HashData(key, message);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

record RegisterRequest(
    [property: JsonPropertyName("instanceUrl")] string? InstanceUrl);

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
