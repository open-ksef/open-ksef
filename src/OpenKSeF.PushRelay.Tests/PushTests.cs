using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenKSeF.PushRelay.Tests;

public sealed class PushTests : IDisposable
{
    private const string RegisterBody = """{"instanceUrl":"https://instance.invalid"}""";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "relay-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFcm _fcm = new();
    private readonly FakeApnsHandler _apns = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PushTests()
    {
        var apnsConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["APNs:KeyId"] = "test", ["APNs:BaseUrl"] = "https://apns.invalid" })
            .Build();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Relay:DataDir", _dataDir)
            .ConfigureTestServices(s =>
            {
                s.AddSingleton<FcmSender>(_fcm);
                s.AddSingleton(new ApnsClient(apnsConfig, NullLogger<ApnsClient>.Instance, _apns));
            }));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Push_WithValidSignature_Sends()
    {
        var instance = await Register();

        var response = await Push(instance.InstanceId, instance.ApiKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("t", _fcm.Sent.Single().Notification.Title);
    }

    [Fact]
    public async Task Push_WithWrongKey_Returns401()
    {
        var instance = await Register();
        var other = await Register();

        var response = await Push(instance.InstanceId, other.ApiKey);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_fcm.Sent);
    }

    [Fact]
    public async Task Push_WithStaleTimestamp_Returns401()
    {
        var instance = await Register();

        var response = await Push(instance.InstanceId, instance.ApiKey, at: DateTimeOffset.UtcNow.AddMinutes(-6));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_fcm.Sent);
    }

    [Fact]
    public async Task Push_IsRateLimitedPerInstance()
    {
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Push("unknown-instance", "key")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Push("unknown-instance", "key")).StatusCode);
    }

    [Theory]
    [InlineData(MessagingErrorCode.InvalidArgument, "Invalid JSON payload received.", HttpStatusCode.BadGateway)]
    [InlineData(MessagingErrorCode.InvalidArgument, "The registration token is not a valid FCM registration token", HttpStatusCode.Gone)]
    [InlineData(MessagingErrorCode.Unregistered, "Requested entity was not found.", HttpStatusCode.Gone)]
    [InlineData(MessagingErrorCode.Internal, "Internal error", HttpStatusCode.BadGateway)]
    public async Task Push_FcmError_MapsToTokenStatus(MessagingErrorCode code, string message, HttpStatusCode expected)
    {
        var instance = await Register();
        _fcm.Error = FcmException(code, message);

        var response = await Push(instance.InstanceId, instance.ApiKey);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "", HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest, """{"reason":"BadDeviceToken"}""", HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.Gone, """{"reason":"Unregistered"}""", HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest, """{"reason":"BadTopic"}""", HttpStatusCode.BadGateway)]
    public async Task Push_ApnsResponse_MapsToTokenStatus(HttpStatusCode apnsStatus, string apnsBody, HttpStatusCode expected)
    {
        var instance = await Register();
        _apns.Response = (apnsStatus, apnsBody);

        var response = await Push(instance.InstanceId, instance.ApiKey, platform: "ios");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("/3/device/token", _apns.Paths.Single());
        Assert.Empty(_fcm.Sent);
    }

    private async Task<(string InstanceId, string ApiKey)> Register()
    {
        var response = await _client.PostAsync("/api/register", new StringContent(RegisterBody, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("instanceId").GetString()!, json.GetProperty("apiKey").GetString()!);
    }

    private async Task<HttpResponseMessage> Push(string instanceId, string apiKey, DateTimeOffset? at = null, string platform = "android")
    {
        var body = $$"""{"pushToken":"token","platform":"{{platform}}","title":"t","body":"b"}""";
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/push") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Relay-Instance", instanceId);
        request.Headers.Add("X-Relay-Timestamp", timestamp);
        request.Headers.Add("X-Relay-Signature", signature);
        return await _client.SendAsync(request);
    }

    private static FirebaseMessagingException FcmException(MessagingErrorCode code, string message)
    {
        var ctor = typeof(FirebaseMessagingException).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).First();
        var args = ctor.GetParameters().Select(p =>
            p.ParameterType == typeof(ErrorCode) ? ErrorCode.InvalidArgument
            : p.ParameterType == typeof(string) ? message
            : p.ParameterType == typeof(MessagingErrorCode?) ? (object)code
            : null).ToArray();
        return (FirebaseMessagingException)ctor.Invoke(args);
    }

    private sealed class FakeFcm : FcmSender
    {
        public List<Message> Sent { get; } = [];

        public Exception? Error { get; set; }

        public override bool Enabled => true;

        public override Task<string> SendAsync(Message message)
        {
            if (Error is not null)
                throw Error;
            Sent.Add(message);
            return Task.FromResult("message-id");
        }
    }

    private sealed class FakeApnsHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public (HttpStatusCode Status, string Body) Response { get; set; } = (HttpStatusCode.OK, "");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(Response.Status) { Content = new StringContent(Response.Body) });
        }
    }
}
