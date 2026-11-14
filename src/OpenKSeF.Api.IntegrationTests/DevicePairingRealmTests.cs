using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class DevicePairingRealmTests : IAsyncLifetime
{
    private static readonly string[] RealmPolicyKeys =
    [
        "displayName", "internationalizationEnabled", "supportedLocales", "defaultLocale", "accessTokenLifespan",
        "offlineSessionIdleTimeout", "offlineSessionMaxLifespanEnabled", "offlineSessionMaxLifespan",
        "eventsEnabled", "eventsExpiration", "enabledEventTypes", "loginTheme", "accountTheme",
        "otpPolicyType", "otpPolicyAlgorithm", "otpPolicyDigits", "otpPolicyPeriod", "otpPolicyLookAheadWindow",
        "browserFlow", "registrationFlow", "resetCredentialsFlow",
    ];

    private static readonly string[] ClientPolicyAttributes =
        ["oauth2.device.authorization.grant.enabled", "oauth2.device.code.lifespan", "pkce.code.challenge.method"];

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;

    public DevicePairingRealmTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SettingsSave_AppliesPolicyOnce_KeepsOtherRealmAndClientFields()
    {
        var realmBefore = await GetAdminAsync("");
        var clientBefore = await GetMobileClientAsync();

        await SaveSettingsAsync();
        var realmAfter = await GetAdminAsync("");
        var clientAfter = await GetMobileClientAsync();
        var scopesAfter = await GetAdminAsync($"/clients/{clientAfter["id"]}/optional-client-scopes");

        await SaveSettingsAsync();
        Assert.True(JsonNode.DeepEquals(realmAfter, await GetAdminAsync("")));
        Assert.True(JsonNode.DeepEquals(clientAfter, await GetMobileClientAsync()));
        Assert.True(JsonNode.DeepEquals(scopesAfter, await GetAdminAsync($"/clients/{clientAfter["id"]}/optional-client-scopes")));

        foreach (var (key, value) in realmBefore.AsObject().Where(p => !RealmPolicyKeys.Contains(p.Key)))
            Assert.True(JsonNode.DeepEquals(value, realmAfter[key]), $"realm.{key} changed");
        foreach (var (key, value) in clientBefore.AsObject().Where(p => p.Key is not ("name" or "attributes")))
            Assert.True(JsonNode.DeepEquals(value, clientAfter[key]), $"client.{key} changed");
        foreach (var (key, value) in clientBefore["attributes"]!.AsObject().Where(p => !ClientPolicyAttributes.Contains(p.Key)))
            Assert.True(JsonNode.DeepEquals(value, clientAfter["attributes"]![key]), $"client.attributes.{key} changed");

        Assert.Equal("OpenKSeF", (string?)realmAfter["displayName"]);
        Assert.Equal(["pl"], realmAfter["supportedLocales"]!.AsArray().Select(l => (string?)l));
        Assert.Equal("pl", (string?)realmAfter["defaultLocale"]);
        Assert.Equal(300, (int?)realmAfter["accessTokenLifespan"]);
        Assert.Equal(2592000, (int?)realmAfter["offlineSessionIdleTimeout"]);
        Assert.Equal(31536000, (int?)realmAfter["offlineSessionMaxLifespan"]);
        Assert.True((bool?)realmAfter["eventsEnabled"]);
        Assert.Equal(2592000, (long?)realmAfter["eventsExpiration"]);
        Assert.Equal(
            ["LOGIN", "LOGIN_ERROR", "OAUTH2_DEVICE_CODE_TO_TOKEN", "OAUTH2_DEVICE_VERIFY_USER_CODE", "REMOVE_CREDENTIAL", "REMOVE_TOTP", "REVOKE_GRANT", "UPDATE_CREDENTIAL", "UPDATE_TOTP"],
            realmAfter["enabledEventTypes"]!.AsArray().Select(e => (string)e!).Order());
        Assert.Equal("Aplikacja mobilna OpenKSeF", (string?)clientAfter["name"]);
        Assert.Equal("true", (string?)clientAfter["attributes"]!["oauth2.device.authorization.grant.enabled"]);
        Assert.Equal("300", (string?)clientAfter["attributes"]!["oauth2.device.code.lifespan"]);
        Assert.Equal("S256", (string?)clientAfter["attributes"]!["pkce.code.challenge.method"]);
        Assert.True((bool?)clientAfter["directAccessGrantsEnabled"]);
        Assert.Contains(scopesAfter.AsArray(), s => (string?)s!["name"] == "offline_access");

        var texts = (await GetAdminAsync("/localization/pl")).AsObject();
        Assert.Equal(SystemSetupService.PolishLoginTexts.OrderBy(p => p.Key), texts.Select(p => KeyValuePair.Create(p.Key, (string)p.Value!)).OrderBy(p => p.Key));
    }

    [Fact]
    public async Task SettingsSave_DiscoveryListsDeviceEndpoint()
    {
        await SaveSettingsAsync();
        using var http = new HttpClient();

        var discovery = await http.GetFromJsonAsync<JsonElement>($"{_fixture.KeycloakAuthority}/.well-known/openid-configuration");

        Assert.Equal($"{_fixture.KeycloakAuthority}/protocol/openid-connect/auth/device", discovery.GetProperty("device_authorization_endpoint").GetString());
    }

    [Fact]
    public async Task DeviceRequest_RequiresPkce_ThenPollIsPending()
    {
        await SaveSettingsAsync();
        using var http = new HttpClient();
        var deviceUrl = $"{_fixture.KeycloakAuthority}/protocol/openid-connect/auth/device";

        var withoutPkce = await http.PostAsync(deviceUrl, Form(("client_id", "openksef-mobile"), ("scope", "openid offline_access")));
        Assert.Equal(HttpStatusCode.BadRequest, withoutPkce.StatusCode);
        Assert.Equal("invalid_request", (await withoutPkce.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var (verifier, challenge) = NewPkce();
        var withPkce = await http.PostAsync(deviceUrl, Form(
            ("client_id", "openksef-mobile"), ("scope", "openid offline_access"), ("code_challenge", challenge), ("code_challenge_method", "S256")));
        Assert.Equal(HttpStatusCode.OK, withPkce.StatusCode);
        var device = await withPkce.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(300, device.GetProperty("expires_in").GetInt32());

        await Task.Delay(TimeSpan.FromSeconds(device.GetProperty("interval").GetInt32() + 1));
        var poll = await http.PostAsync($"{_fixture.KeycloakAuthority}/protocol/openid-connect/token", Form(
            ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"), ("client_id", "openksef-mobile"),
            ("device_code", device.GetProperty("device_code").GetString()!), ("code_verifier", verifier)));

        Assert.Equal(HttpStatusCode.BadRequest, poll.StatusCode);
        Assert.Equal("authorization_pending", (await poll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task MobileClient_PasswordGrantStillWorks_With300SecondToken()
    {
        await SaveSettingsAsync();

        var token = await _fixture.GetAccessTokenAsync();

        var payload = JwtPayload(token);
        Assert.Equal(300, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        Assert.Contains("openksef-api", TestcontainersFixture.AccessTokenAudiences(token));
    }

    [Fact]
    public async Task MobileClient_CodeWithPkceStillWorks()
    {
        await SaveSettingsAsync();
        const string redirectUri = "openksef://auth/callback";
        var (verifier, challenge) = NewPkce();
        var email = $"t406-{Guid.NewGuid():N}@openksef.test";
        await _fixture.CreateUserAsync(email);
        using var browser = new KeycloakBrowser(redirectUri);

        await browser.GetAsync(
            $"{_fixture.KeycloakAuthority}/protocol/openid-connect/auth?client_id=openksef-mobile&response_type=code&scope=openid" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&code_challenge={challenge}&code_challenge_method=S256&state=t406");
        await browser.SubmitLoginAsync(email, "Test1234!");
        await browser.EnrolAsync();
        Assert.False(string.IsNullOrEmpty(browser.Code));

        using var http = new HttpClient();
        var tokens = await http.PostAsync($"{_fixture.KeycloakAuthority}/protocol/openid-connect/token", Form(
            ("grant_type", "authorization_code"), ("client_id", "openksef-mobile"), ("code", browser.Code!),
            ("redirect_uri", redirectUri), ("code_verifier", verifier)));

        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode);
        Assert.Contains("openksef-api", TestcontainersFixture.AccessTokenAudiences(
            (await tokens.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!));
    }

    [Fact]
    public async Task SetupTokenRoutes_Return404()
    {
        using var client = _factory.CreateClient();
        client.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var generate = await client.PostAsync("/api/account/setup-token", null);
        var redeem = await client.PostAsJsonAsync("/api/account/redeem-setup-token", new { setupToken = "x" });

        Assert.Equal(HttpStatusCode.NotFound, generate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
    }

    private async Task SaveSettingsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().UpdateSettingsAsync(
            new SettingsUpdateRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" },
            await _fixture.GetAdminTokenAsync());
        Assert.True(result.Success, result.Error);
    }

    private async Task<JsonNode> GetAdminAsync(string path)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _fixture.GetAdminTokenAsync());
        return JsonNode.Parse(await http.GetStringAsync($"{_fixture.Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/admin/realms/openksef{path}"))!;
    }

    private async Task<JsonNode> GetMobileClientAsync() =>
        (await GetAdminAsync("/clients?clientId=openksef-mobile")).AsArray().Single()!;

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => KeyValuePair.Create(f.Key, f.Value)));

    private static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonElement JwtPayload(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '='))).RootElement;
    }
}
