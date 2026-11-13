using System.Net;
using System.Text;
using System.Text.Json;
using KSeF.Client.Api.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class KeycloakRealmPayloadTests
{
    private const string RealmUrl = "http://keycloak:8080/auth/admin/realms/openksef";
    private const string ValidKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string UnpaddedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";

    private readonly FakeKeycloak _keycloak = new();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();

    public KeycloakRealmPayloadTests()
    {
        _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_keycloak));
    }

    [Fact]
    public async Task ApplySetup_NewRealm_CreatesRealmWithBruteForceAndClientsWithoutWildcardOrigins()
    {
        await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey)
            .ApplySetupAsync(NewSetupRequest(), "token", "admin");

        var realm = _keycloak.Body(HttpMethod.Post, "http://keycloak:8080/auth/admin/realms");
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Equal(10, realm.GetProperty("failureFactor").GetInt32());

        var clients = _keycloak.Bodies(HttpMethod.Post, $"{RealmUrl}/clients");
        Assert.Equal(3, clients.Count);
        Assert.All(clients, c => Assert.Equal(["+"], c.GetProperty("webOrigins").EnumerateArray().Select(o => o.GetString())));
    }

    [Theory]
    [InlineData("""{"error":"invalidPasswordMinLengthMessage","error_description":"Invalid password: minimum length 12."}""", SystemSetupService.KeycloakPasswordPolicyMessage)]
    [InlineData("""{"errorMessage":"Conflict detected. See logs for details"}""", SystemSetupService.KeycloakRejectedMessage)]
    public async Task ApplySetup_KeycloakRejectsAWrite_ReturnsPolishMessage_NotTheHttpStatusText(string keycloakBody, string expected)
    {
        _keycloak.WriteFailureBody = keycloakBody;

        var result = await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey).ApplySetupAsync(NewSetupRequest(), "token", "admin");

        Assert.False(result.Success);
        Assert.Equal(expected, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false, true, false)]
    [InlineData(HttpStatusCode.Unauthorized, false, false, false)]
    [InlineData(HttpStatusCode.BadRequest, false, false, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, false, true)]
    [InlineData(HttpStatusCode.OK, true, false, true)]
    public async Task AuthenticateAdmin_SeparatesRejectedCredentialsFromTransportFailure(HttpStatusCode tokenStatus, bool unreachable, bool expectToken, bool expectUnreachable)
    {
        _keycloak.TokenStatus = tokenStatus;
        _keycloak.Unreachable = unreachable;

        var (token, isUnreachable) = await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey).AuthenticateAdminAsync("admin", "secret");

        Assert.Equal(expectToken, token is not null);
        Assert.Equal(expectUnreachable, isUnreachable);
    }

    [Fact]
    public async Task ApplySetup_KeycloakUnreachable_ReturnsPolishConnectionMessage()
    {
        _keycloak.Unreachable = true;

        var result = await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey).ApplySetupAsync(NewSetupRequest(), "token", "admin");

        Assert.False(result.Success);
        Assert.Equal(SystemSetupService.KeycloakUnreachableMessage, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData(UnpaddedKey)]
    public async Task ApplySetup_NoValidKey_FailsBeforeAnyKeycloakCall(string? key)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();

        var result = await CreateSetupService(systemConfig, key).ApplySetupAsync(NewSetupRequest(), "token", "admin");

        Assert.False(result.Success);
        Assert.Equal(0, _keycloak.RequestCount);
        await systemConfig.DidNotReceiveWithAnyArgs().SetValuesAsync(default!, default);
    }

    [Theory]
    [InlineData("tset")]
    [InlineData("https://ksef.mf.gov.pl/api")]
    public async Task ApplySetup_UnknownEnvironment_FailsBeforeAnyKeycloakCall(string environment)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();

        var result = await CreateSetupService(systemConfig, ValidKey).ApplySetupAsync(
            new SetupApplyRequest
            {
                ExternalBaseUrl = "https://ksef.example.com",
                KSeFBaseUrl = environment,
                AdminEmail = "admin@example.com",
                AdminPassword = "Secret123!",
            }, "token", "admin");

        Assert.False(result.Success);
        Assert.Contains("Unknown KSeF environment", result.Error);
        Assert.Equal(0, _keycloak.RequestCount);
        await systemConfig.DidNotReceiveWithAnyArgs().SetValuesAsync(default!, default);
    }

    [Theory]
    [InlineData("tset")]
    [InlineData("https://ksef.mf.gov.pl/api")]
    public async Task UpdateSettings_UnknownEnvironment_FailsBeforeAnyKeycloakCall(string environment)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        var options = new KSeFClientOptions { BaseUrl = KsefEnvironmentsUris.PROD };

        var result = await CreateSettingsService(systemConfig, options).UpdateSettingsAsync(
            new SettingsUpdateRequest { KSeFEnvironment = environment }, "token");

        Assert.False(result.Success);
        Assert.Contains("Unknown KSeF environment", result.Error);
        Assert.Equal(0, _keycloak.RequestCount);
        Assert.Equal(KsefEnvironmentsUris.PROD, options.BaseUrl);
        await systemConfig.DidNotReceiveWithAnyArgs().SetValuesAsync(default!, default);
    }

    [Fact]
    public async Task UpdateSettings_BlankEnvironment_KeepsTheStoredOne()
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment).Returns("production");
        var options = new KSeFClientOptions { BaseUrl = KsefEnvironmentsUris.PROD };

        var result = await CreateSettingsService(systemConfig, options).UpdateSettingsAsync(
            new SettingsUpdateRequest { KSeFEnvironment = "   " }, "token");

        Assert.True(result.Success, result.Error);
        Assert.Equal(KsefEnvironmentsUris.PROD, options.BaseUrl);
        await systemConfig.DidNotReceiveWithAnyArgs().SetValuesAsync(default!, default);
    }

    [Fact]
    public async Task UpdateSettings_BruteForceDisabled_EnablesItAndRestrictsWebOrigins()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":false}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { ExternalBaseUrl = "https://ksef.example.com/" }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.RealmUpdate();
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Equal(10, realm.GetProperty("failureFactor").GetInt32());

        var clientUpdates = _keycloak.Bodies(HttpMethod.Put, $"{RealmUrl}/clients/uuid-", prefix: true).Where(c => c.TryGetProperty("webOrigins", out _)).ToList();
        Assert.Equal(3, clientUpdates.Count);
        Assert.All(clientUpdates, c => Assert.Equal(
            ["https://ksef.example.com"], c.GetProperty("webOrigins").EnumerateArray().Select(o => o.GetString())));
    }

    [Fact]
    public async Task ApplySetup_AppliesRealmPolicyAfterConfigureRealm()
    {
        _keycloak.RealmJson = "{}";

        var result = await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey).ApplySetupAsync(NewSetupRequest(), "token", "admin");

        Assert.True(result.Success, result.Error);
        Assert.Equal(300, _keycloak.RealmUpdate().GetProperty("accessTokenLifespan").GetInt32());
        var realmPuts = _keycloak.Requests.Where(r => r.Method == HttpMethod.Put && r.Url == RealmUrl).ToList();
        Assert.Equal(2, realmPuts.Count);
        var bodies = _keycloak.Bodies(HttpMethod.Put, RealmUrl);
        Assert.True(bodies[0].TryGetProperty("registrationAllowed", out _));
        Assert.True(bodies[1].TryGetProperty("loginTheme", out _));
        AssertRealmPolicy();
    }

    [Fact]
    public async Task UpdateSettings_NoExternalBaseUrl_AppliesRealmPolicy()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        AssertRealmPolicy();
        Assert.DoesNotContain(_keycloak.Requests, r => r.Url.Contains("/optional-client-scopes/"));
    }

    [Fact]
    public async Task UpdateSettings_OfflineAccessNotOptional_AddsItToMobileClient()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10}""";
        _keycloak.OptionalScopesJson = """[{"id":"scope-roles","name":"roles"}]""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        Assert.Contains((HttpMethod.Put, $"{RealmUrl}/clients/uuid-openksef-mobile/optional-client-scopes/scope-offline"), _keycloak.Requests);
    }

    private void AssertRealmPolicy()
    {
        var realm = _keycloak.RealmPolicy();
        Assert.Equal("openksef", realm.GetProperty("loginTheme").GetString());
        Assert.Equal("OpenKSeF", realm.GetProperty("displayName").GetString());
        Assert.True(realm.GetProperty("internationalizationEnabled").GetBoolean());
        Assert.Equal(["pl"], realm.GetProperty("supportedLocales").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("pl", realm.GetProperty("defaultLocale").GetString());
        Assert.Equal(300, realm.GetProperty("accessTokenLifespan").GetInt32());
        Assert.Equal(2592000, realm.GetProperty("offlineSessionIdleTimeout").GetInt32());
        Assert.True(realm.GetProperty("offlineSessionMaxLifespanEnabled").GetBoolean());
        Assert.Equal(31536000, realm.GetProperty("offlineSessionMaxLifespan").GetInt32());
        Assert.True(realm.GetProperty("eventsEnabled").GetBoolean());
        Assert.Equal(2592000, realm.GetProperty("eventsExpiration").GetInt32());
        Assert.Equal(
            ["LOGIN", "LOGIN_ERROR", "OAUTH2_DEVICE_VERIFY_USER_CODE", "OAUTH2_DEVICE_CODE_TO_TOKEN", "REVOKE_GRANT"],
            realm.GetProperty("enabledEventTypes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(12, realm.EnumerateObject().Count());

        Assert.Equal(
            SystemSetupService.PolishLoginTexts.Keys.Select(k => $"{RealmUrl}/localization/pl/{k}").Order(),
            _keycloak.Requests.Where(r => r.Method == HttpMethod.Put && r.Url.Contains("/localization/pl/")).Select(r => r.Url).Order());
        Assert.Equal(12, SystemSetupService.PolishLoginTexts.Count);

        var mobile = Assert.Single(_keycloak.Bodies(HttpMethod.Put, $"{RealmUrl}/clients/uuid-openksef-mobile"), b => b.TryGetProperty("attributes", out _));
        Assert.Equal(["name", "attributes"], mobile.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Aplikacja mobilna OpenKSeF", mobile.GetProperty("name").GetString());
        var attributes = mobile.GetProperty("attributes");
        Assert.Equal("true", attributes.GetProperty("oauth2.device.authorization.grant.enabled").GetString());
        Assert.Equal("300", attributes.GetProperty("oauth2.device.code.lifespan").GetString());
        Assert.Equal("S256", attributes.GetProperty("pkce.code.challenge.method").GetString());
    }

    private static SmtpConfig Smtp(string? password) =>
        new() { Host = "smtp.example.com", From = "noreply@example.com", Auth = true, User = "mailer", Password = password };

    [Theory]
    [InlineData(null, "**********")]
    [InlineData("", "**********")]
    [InlineData("typed-password", "typed-password")]
    public async Task UpdateSettings_SmtpPassword_MaskKeepsStoredOneUnlessTyped(string? password, string sent)
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest { Smtp = Smtp(password) }, "token");

        Assert.True(result.Success, result.Error);
        Assert.Equal(sent, _keycloak.RealmUpdate().GetProperty("smtpServer").GetProperty("password").GetString());
    }

    [Theory]
    [InlineData(null, "**********")]
    [InlineData("typed-password", "typed-password")]
    public async Task ApplySetup_SmtpPassword_MaskUnlessTyped(string? password, string sent)
    {
        _keycloak.RealmJson = "{}";

        await CreateSetupService(Substitute.For<ISystemConfigService>(), ValidKey)
            .ApplySetupAsync(NewSetupRequest() with { Smtp = Smtp(password) }, "token", "admin");

        var realm = _keycloak.Bodies(HttpMethod.Put, RealmUrl).Single(b => b.TryGetProperty("smtpServer", out _));
        Assert.Equal(sent, realm.GetProperty("smtpServer").GetProperty("password").GetString());
    }

    [Fact]
    public async Task UpdateSettings_BruteForceAlreadyEnabled_KeepsAdminValues()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":3}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { RegistrationAllowed = false }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.RealmUpdate();
        Assert.False(realm.TryGetProperty("bruteForceProtected", out _));
        Assert.False(realm.TryGetProperty("failureFactor", out _));
    }

    [Fact]
    public async Task UpdateSettings_BruteForceEnabledWithHighFailureFactor_LowersOnlyFailureFactor()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":30,"permanentLockout":false,"waitIncrementSeconds":120}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.RealmUpdate();
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
        var realm = _keycloak.RealmUpdate();
        Assert.False(realm.GetProperty("permanentLockout").GetBoolean());
        Assert.Single(realm.EnumerateObject());
    }

    [Fact]
    public async Task UpdateSettings_RealmAlreadyCompliant_SendsNoRealmUpdate()
    {
        _keycloak.RealmJson = """{"bruteForceProtected":true,"failureFactor":10}""";

        var result = await CreateSettingsService().UpdateSettingsAsync(new SettingsUpdateRequest(), "token");

        Assert.True(result.Success, result.Error);
        Assert.Empty(_keycloak.RealmUpdates());
    }

    [Fact]
    public async Task UpdateSettings_RealmReadFails_AppliesRequestedChangesWithoutBruteForce()
    {
        _keycloak.RealmJson = "{}";
        _keycloak.RealmStatus = HttpStatusCode.Forbidden;

        var result = await CreateSettingsService().UpdateSettingsAsync(
            new SettingsUpdateRequest { RegistrationAllowed = false }, "token");

        Assert.True(result.Success, result.Error);
        var realm = _keycloak.RealmUpdate();
        Assert.False(realm.GetProperty("registrationAllowed").GetBoolean());
        Assert.False(realm.TryGetProperty("bruteForceProtected", out _));
        Assert.False(realm.TryGetProperty("failureFactor", out _));
    }

    [Fact]
    public async Task ApplySetup_StoresRegistrationFlag()
    {
        _keycloak.RealmJson = "{}";
        var systemConfig = Substitute.For<ISystemConfigService>();

        var result = await CreateSetupService(systemConfig, ValidKey)
            .ApplySetupAsync(NewSetupRequest() with { RegistrationAllowed = false }, "token", "admin");

        Assert.True(result.Success, result.Error);
        Assert.Equal("openksef-admin", _keycloak.Body(HttpMethod.Post, $"{RealmUrl}/roles").GetProperty("name").GetString());
        Assert.Equal("role-1", _keycloak.Body(HttpMethod.Post, $"{RealmUrl}/users/admin-1/role-mappings/realm")[0].GetProperty("id").GetString());

        await systemConfig.Received().SetValuesAsync(
            Arg.Is<IDictionary<string, string>>(v => v[SystemConfigKeys.RegistrationAllowed] == "false"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task UpdateSettings_RegistrationToggle_StoresRegistrationFlag(bool allowed, string stored)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();

        var result = await CreateSettingsService(systemConfig).UpdateSettingsAsync(
            new SettingsUpdateRequest { RegistrationAllowed = allowed }, "token");

        Assert.True(result.Success, result.Error);
        await systemConfig.Received().SetValuesAsync(
            Arg.Is<IDictionary<string, string>>(v => v[SystemConfigKeys.RegistrationAllowed] == stored),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://relay.invalid/", true)]
    [InlineData("https://other-relay.invalid", false)]
    public async Task UpdateSettings_ReRegisterRelay_SignsWithTheOldKeyOnlyForTheSameRelay(string requestedRelay, bool expectProof)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.PushRelayUrl).Returns("https://relay.invalid");
        systemConfig.GetValue(SystemConfigKeys.PushRelayInstanceId).Returns("old-instance");
        systemConfig.GetValue(SystemConfigKeys.PushRelayApiKey).Returns("old-key");
        systemConfig.GetValue(SystemConfigKeys.ExternalBaseUrl).Returns("https://instance.invalid");

        var result = await CreateSettingsService(systemConfig).UpdateSettingsAsync(
            new SettingsUpdateRequest { PushRelayUrl = requestedRelay, ReRegisterRelay = true }, "token");

        Assert.True(result.Success, result.Error);
        var (body, headers) = _keycloak.Register!.Value;
        Assert.Equal(expectProof, headers.Contains("X-Relay-Instance"));
        if (expectProof)
        {
            Assert.Equal("old-instance", headers.GetValues("X-Relay-Instance").Single());
            var timestamp = headers.GetValues("X-Relay-Timestamp").Single();
            Assert.Equal(RelayPushProvider.ComputeHmac(body, $"register.{timestamp}", "old-key"), headers.GetValues("X-Relay-Signature").Single());
        }
        await systemConfig.Received().SetValuesAsync(
            Arg.Is<IDictionary<string, string>>(v => v[SystemConfigKeys.PushRelayInstanceId] == "new-instance" && v[SystemConfigKeys.PushRelayApiKey] == "new-key"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, "typed-key", true)]
    [InlineData(false, null, false)]
    public async Task UpdateSettings_NewRelayKey_ClearsTheRelayFailure(bool reRegister, string? typedKey, bool cleared)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.ExternalBaseUrl).Returns("https://instance.invalid");
        var health = new RelayHealth();
        health.Failed(403);

        var result = await CreateSettingsService(systemConfig, relayHealth: health).UpdateSettingsAsync(
            new SettingsUpdateRequest { PushRelayUrl = "https://relay.invalid", ReRegisterRelay = reRegister, PushRelayApiKey = typedKey }, "token");

        Assert.True(result.Success, result.Error);
        Assert.Equal(cleared, health.LastFailure is null);
    }

    private SystemSetupService CreateSetupService(ISystemConfigService systemConfig, string? encryptionKey) => new(
        _httpClientFactory,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY"] = encryptionKey })
            .Build(),
        systemConfig,
        new KSeFClientOptions(),
        Substitute.For<IServiceScopeFactory>(),
        NullLogger<SystemSetupService>.Instance);

    private static SetupApplyRequest NewSetupRequest() => new()
    {
        ExternalBaseUrl = "https://ksef.example.com",
        KSeFBaseUrl = "test",
        AdminEmail = "admin@example.com",
        AdminPassword = "Secret123!",
    };

    private SystemSettingsService CreateSettingsService(ISystemConfigService? systemConfig = null, KSeFClientOptions? options = null, RelayHealth? relayHealth = null) => new(
        _httpClientFactory, new ConfigurationBuilder().Build(), systemConfig ?? Substitute.For<ISystemConfigService>(), relayHealth ?? new RelayHealth(),
        options ?? new KSeFClientOptions(), Substitute.For<IServiceScopeFactory>(), NullLogger<SystemSettingsService>.Instance);

    private sealed class FakeKeycloak : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Url, string? Body)> _requests = [];

        public int RequestCount => _requests.Count;

        public string? RealmJson { get; set; }

        public HttpStatusCode RealmStatus { get; set; } = HttpStatusCode.OK;

        public string? WriteFailureBody { get; set; }

        public bool Unreachable { get; set; }

        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

        public (string Body, System.Net.Http.Headers.HttpRequestHeaders Headers)? Register { get; private set; }

        public JsonElement Body(HttpMethod method, string url) => Assert.Single(Bodies(method, url));

        public string OptionalScopesJson { get; set; } = """[{"id":"scope-offline","name":"offline_access"}]""";

        public List<(HttpMethod Method, string Url)> Requests => _requests.Select(r => (r.Method, r.Url)).ToList();

        public List<JsonElement> RealmUpdates() => Bodies(HttpMethod.Put, RealmUrl).Where(b => !b.TryGetProperty("loginTheme", out _)).ToList();

        public JsonElement RealmUpdate() => Assert.Single(RealmUpdates());

        public JsonElement RealmPolicy() => Assert.Single(Bodies(HttpMethod.Put, RealmUrl), b => b.TryGetProperty("loginTheme", out _));

        public List<JsonElement> Bodies(HttpMethod method, string url, bool prefix = false) => _requests
            .Where(r => r.Method == method && r.Body != null && (prefix ? r.Url.StartsWith(url) : r.Url == url))
            .Select(r => JsonDocument.Parse(r.Body!).RootElement)
            .ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            _requests.Add((request.Method, url, body));
            if (Unreachable)
                throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (keycloak:8080)");

            if (url.EndsWith("/api/register"))
            {
                Register = (body!, request.Headers);
                return Json("""{"instanceId":"new-instance","apiKey":"new-key"}""");
            }

            if (url.EndsWith("/protocol/openid-connect/token"))
                return TokenStatus == HttpStatusCode.OK ? Json("""{"access_token":"kc-token"}""") : new HttpResponseMessage(TokenStatus);

            if (request.Method != HttpMethod.Get)
                return WriteFailureBody is null ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(WriteFailureBody, Encoding.UTF8, "application/json") };

            if (url == RealmUrl)
                return RealmJson is null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : RealmStatus != HttpStatusCode.OK ? new HttpResponseMessage(RealmStatus) : Json(RealmJson);

            if (url.Contains("/clients?clientId="))
            {
                var clientId = url[(url.IndexOf('=') + 1)..];
                return Json(RealmJson is null ? "[]" : $$"""[{"id":"uuid-{{clientId}}"}]""");
            }

            if (url.Contains("/users?email="))
                return Json("""[{"id":"admin-1"}]""");

            if (url.EndsWith("/available"))
                return Json("[]");

            if (url.EndsWith("/roles/openksef-admin"))
                return Json("""{"id":"role-1","name":"openksef-admin"}""");

            if (url.EndsWith("/protocol-mappers/models"))
                return Json("[]");

            if (url.EndsWith("/optional-client-scopes"))
                return Json(OptionalScopesJson);

            if (url.EndsWith("/client-scopes"))
                return Json("""[{"id":"scope-roles","name":"roles"},{"id":"scope-offline","name":"offline_access"}]""");

            if (url.Contains("/clients/uuid-"))
                return Json("""{"id":"user-1","value":"client-secret","redirectUris":["/*"],"serviceAccountsEnabled":true}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
