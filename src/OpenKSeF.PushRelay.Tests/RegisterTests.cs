using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.PushRelay;

namespace OpenKSeF.PushRelay.Tests;

public sealed class RegisterTests : IDisposable
{
    private const string Body = """{"instanceUrl":"https://instance.invalid"}""";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "relay-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RegisterTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Relay:DataDir", _dataDir));
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

    private async Task<(string InstanceId, string ApiKey)> Register(Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/register") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? [])
            request.Headers.Add(name, value);

        var response = await _client.SendAsync(request);
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
