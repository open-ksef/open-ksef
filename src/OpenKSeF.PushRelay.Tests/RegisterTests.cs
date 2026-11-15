using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.PushRelay;

namespace OpenKSeF.PushRelay.Tests;

public sealed class RegisterTests : IDisposable
{
    private const string Body = """{"instanceUrl":"https://instance.invalid"}""";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "relay-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly WarningLog _logs = new();

    public RegisterTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Relay:DataDir", _dataDir)
            .ConfigureLogging(l => l.AddProvider(_logs)));
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

    private InstanceStore Store => _factory.Services.GetRequiredService<InstanceStore>();

    [Fact]
    public async Task ReRegister_WithValidProof_DisablesTheOldInstance()
    {
        var old = await Register();

        var replacement = await Register(Proof(old.InstanceId, old.ApiKey));

        Assert.NotEqual(old.InstanceId, replacement.InstanceId);
        Assert.False((await Store.GetAsync(old.InstanceId))!.Enabled);
        Assert.True((await Store.GetAsync(replacement.InstanceId))!.Enabled);
        Assert.Equal(HttpStatusCode.Forbidden, (await Push(old.InstanceId, old.ApiKey)).StatusCode);
    }

    [Fact]
    public async Task ReRegister_WithProofFromDisabledInstance_StillIssuesANewInstance()
    {
        var old = await Register();
        await Register(Proof(old.InstanceId, old.ApiKey));

        var recovered = await Register(Proof(old.InstanceId, old.ApiKey));

        Assert.True((await Store.GetAsync(recovered.InstanceId))!.Enabled);
        Assert.False((await Store.GetAsync(old.InstanceId))!.Enabled);
        Assert.Single(_logs.Warnings, w => w.Contains(old.InstanceId));
    }

    [Fact]
    public async Task ConcurrentReRegisters_OfOneInstance_BothSucceedAndDisableOnce()
    {
        var old = await Register();

        var responses = await Task.WhenAll(Send(Proof(old.InstanceId, old.ApiKey)), Send(Proof(old.InstanceId, old.ApiKey)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.False((await Store.GetAsync(old.InstanceId))!.Enabled);
        Assert.Equal(2, (await Store.ListAsync()).Count(i => i.Enabled));
        Assert.Single(_logs.Warnings, w => w.Contains(old.InstanceId));
    }

    [Fact]
    public async Task ReRegister_WithAPushSignature_LeavesTheOldInstanceEnabled()
    {
        var old = await Register();

        await Register(Proof(old.InstanceId, old.ApiKey, prefix: ""));

        Assert.True((await Store.GetAsync(old.InstanceId))!.Enabled);
    }

    [Fact]
    public async Task ReRegister_WithoutProof_LeavesTheOldInstanceEnabled()
    {
        var old = await Register();

        var replacement = await Register();

        Assert.True((await Store.GetAsync(old.InstanceId))!.Enabled);
        Assert.True((await Store.GetAsync(replacement.InstanceId))!.Enabled);
    }

    [Fact]
    public async Task ReRegister_WithWrongKey_LeavesTheOldInstanceEnabled()
    {
        var old = await Register();
        var other = await Register();

        await Register(Proof(old.InstanceId, other.ApiKey));

        Assert.True((await Store.GetAsync(old.InstanceId))!.Enabled);
    }

    [Fact]
    public async Task ReRegister_WithStaleTimestamp_LeavesTheOldInstanceEnabled()
    {
        var old = await Register();

        await Register(Proof(old.InstanceId, old.ApiKey, DateTimeOffset.UtcNow.AddMinutes(-6)));

        Assert.True((await Store.GetAsync(old.InstanceId))!.Enabled);
    }

    [Fact]
    public async Task Register_IsStillRateLimited()
    {
        for (var i = 0; i < 5; i++)
            await Register();

        var response = await _client.PostAsync("/api/register", new StringContent(Body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Register_LimitFollowsCfConnectingIp_FromKnownProxyOnly()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Relay:DataDir", Path.Combine(_dataDir, "cf"))
            .ConfigureTestServices(s => s.AddSingleton<IStartupFilter, PeerFromTestHeader>()));
        using var client = factory.CreateClient();

        async Task<HttpStatusCode> RegisterVia(string peer, string clientIp)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/register") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
            request.Headers.Add(PeerFromTestHeader.Header, peer);
            request.Headers.Add("CF-Connecting-IP", clientIp);
            return (await client.SendAsync(request)).StatusCode;
        }

        foreach (var clientIp in new[] { "198.51.100.1", "198.51.100.2" })
        {
            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.OK, await RegisterVia("10.0.0.5", clientIp));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterVia("10.0.0.5", "198.51.100.1"));

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, await RegisterVia("203.0.113.9", $"198.51.100.{100 + i}"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await RegisterVia("203.0.113.9", "198.51.100.200"));
    }

    private sealed class WarningLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Warnings { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Enqueue(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }

    private sealed class PeerFromTestHeader : IStartupFilter
    {
        public const string Header = "X-Test-Peer";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[Header].ToString());
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private async Task<HttpResponseMessage> Send(Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/register") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? [])
            request.Headers.Add(name, value);
        return await _client.SendAsync(request);
    }

    private async Task<(string InstanceId, string ApiKey)> Register(Dictionary<string, string>? headers = null)
    {
        var response = await Send(headers);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("instanceId").GetString()!, json.GetProperty("apiKey").GetString()!);
    }

    private async Task<HttpResponseMessage> Push(string instanceId, string apiKey)
    {
        const string body = """{"pushToken":"token","platform":"android"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/push") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in Proof(instanceId, apiKey, body: body, prefix: ""))
            request.Headers.Add(name, value);
        return await _client.SendAsync(request);
    }

    private static Dictionary<string, string> Proof(string instanceId, string apiKey, DateTimeOffset? at = null, string body = Body, string prefix = "register.")
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes($"{prefix}{timestamp}.{body}"))).ToLowerInvariant();
        return new() { ["X-Relay-Instance"] = instanceId, ["X-Relay-Timestamp"] = timestamp, ["X-Relay-Signature"] = signature };
    }
}
