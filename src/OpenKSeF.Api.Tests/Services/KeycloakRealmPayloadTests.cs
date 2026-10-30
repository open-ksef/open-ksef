using System.Net;
using System.Text;
using System.Text.Json;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class KeycloakRealmPayloadTests
{
    private const string RealmUrl = "http://keycloak:8080/auth/admin/realms/openksef";

    private readonly FakeKeycloak _keycloak = new();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();

    public KeycloakRealmPayloadTests()
    {
        _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_keycloak));
    }

    [Fact]
    public async Task ApplySetup_NewRealm_CreatesRealmWithBruteForceAndClientsWithoutWildcardOrigins()
    {
        var service = new SystemSetupService(
            _httpClientFactory, new ConfigurationBuilder().Build(), Substitute.For<ISystemConfigService>(),
            new KSeFClientOptions(), Substitute.For<IServiceScopeFactory>(), NullLogger<SystemSetupService>.Instance);

        await service.ApplySetupAsync(new SetupApplyRequest
        {
            ExternalBaseUrl = "https://ksef.example.com",
            KSeFBaseUrl = "test",
            AdminEmail = "admin@example.com",
            AdminPassword = "Secret123!",
        }, "token", "admin");

        var realm = _keycloak.Body(HttpMethod.Post, "http://keycloak:8080/auth/admin/realms");
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Equal(10, realm.GetProperty("failureFactor").GetInt32());

        var clients = _keycloak.Bodies(HttpMethod.Post, $"{RealmUrl}/clients");
        Assert.Equal(3, clients.Count);
        Assert.All(clients, c => Assert.Equal(["+"], c.GetProperty("webOrigins").EnumerateArray().Select(o => o.GetString())));
    }

    [Fact]
    public async Task UpdateSettings_BruteForceDisabled_EnablesItAndRestrictsWebOrigins()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":false}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { ExternalBaseUrl = "https://ksef.example.com/" }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.Body(HttpMethod.Put, RealmUrl);
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Equal(10, realm.GetProperty("failureFactor").GetInt32());

        var clientUpdates = _keycloak.Bodies(HttpMethod.Put, $"{RealmUrl}/clients/uuid-", prefix: true);
        Assert.Equal(3, clientUpdates.Count);
        Assert.All(clientUpdates, c => Assert.Equal(
            ["https://ksef.example.com"], c.GetProperty("webOrigins").EnumerateArray().Select(o => o.GetString())));
    }

    [Fact]
    public async Task UpdateSettings_BruteForceAlreadyEnabled_KeepsAdminValues()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":3}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { RegistrationAllowed = false }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.Body(HttpMethod.Put, RealmUrl);
        Assert.False(realm.TryGetProperty("bruteForceProtected", out _));
        Assert.False(realm.TryGetProperty("failureFactor", out _));
    }

    [Fact]
    public async Task UpdateSettings_BruteForceEnabledWithHighFailureFactor_LowersOnlyFailureFactor()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":30,"permanentLockout":false,"waitIncrementSeconds":120}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.Body(HttpMethod.Put, RealmUrl);
        Assert.Equal(10, realm.GetProperty("failureFactor").GetInt32());
        Assert.False(realm.TryGetProperty("permanentLockout", out _));
        Assert.Single(realm.EnumerateObject());
    }

    [Fact]
    public async Task UpdateSettings_BruteForceEnabledWithPermanentLockout_SwitchesToTemporaryLockout()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10,"permanentLockout":true}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.Body(HttpMethod.Put, RealmUrl);
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Single(realm.EnumerateObject());
    }

    [Fact]
    public async Task UpdateSettings_RealmAlreadyCompliant_SendsNoRealmUpdate()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        Assert.Empty(_keycloak.Bodies(HttpMethod.Put, RealmUrl));
    }

    [Fact]
    public async Task UpdateSettings_RealmReadFails_AppliesRequestedChangesWithoutBruteForce()
    {
        _keycloak.RealmJson = "{}";
        _keycloak.RealmStatus = HttpStatusCode.Forbidden;

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { RegistrationAllowed = false }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.Body(HttpMethod.Put, RealmUrl);
        Assert.False(realm.GetProperty("registrationAllowed").GetBoolean());
        Assert.False(realm.TryGetProperty("bruteForceProtected", out _));
        Assert.False(realm.TryGetProperty("failureFactor", out _));
    }

    private SystemSettingsService CreateSettingsService() => new(
        _httpClientFactory, new ConfigurationBuilder().Build(), Substitute.For<ISystemConfigService>(),
        new KSeFClientOptions(), Substitute.For<IServiceScopeFactory>(), NullLogger<SystemSettingsService>.Instance);

    private sealed class FakeKeycloak : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Url, string? Body)> _requests = [];

        public string? RealmJson { get; set; }

        public HttpStatusCode RealmStatus { get; set; } = HttpStatusCode.OK;

        public JsonElement Body(HttpMethod method, string url) => Assert.Single(Bodies(method, url));

        public List<JsonElement> Bodies(HttpMethod method, string url, bool prefix = false) => _requests
            .Where(r => r.Method == method && r.Body != null && (prefix ? r.Url.StartsWith(url) : r.Url == url))
            .Select(r => JsonDocument.Parse(r.Body!).RootElement)
            .ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            _requests.Add((request.Method, url, body));

            if (request.Method != HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (url == RealmUrl)
                return RealmJson is null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : RealmStatus != HttpStatusCode.OK ? new HttpResponseMessage(RealmStatus) : Json(RealmJson);

            if (url.Contains("/clients?clientId="))
            {
                var clientId = url[(url.IndexOf('=') + 1)..];
                return Json(RealmJson is null ? "[]" : $$"""[{"id":"uuid-{{clientId}}"}]""");
            }

            if (url.Contains("/clients/uuid-"))
                return Json("""{"redirectUris":["/*"]}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
