using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class AccountRegisterApiTests : IAsyncLifetime
{
    private const string Password = "Register1234!";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountRegisterApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => ResetRealmAsync();

    public async Task DisposeAsync()
    {
        await ResetRealmAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task ResetRealmAsync()
    {
        await _fixture.UpdateRealmAsync(new { registrationAllowed = true, verifyEmail = false });
        await _fixture.GrantServiceAccountRolesAsync(TestcontainersFixture.RegistrationRoles);
    }

    [Fact]
    public async Task Register_RegistrationOff_Returns403AndCreatesNoUser()
    {
        await _fixture.UpdateRealmAsync(new { registrationAllowed = false });
        var email = NewEmail();

        var response = await RegisterAsync(_client, email);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("registration_disabled", body.GetProperty("code").GetString());
        Assert.Contains("wyłączona", body.GetProperty("error").GetString());
        Assert.Null(await _fixture.FindUserAsync(email));
    }

    [Fact]
    public async Task Register_RegistrationSwitchedOff_AppliesToTheNextRequest()
    {
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(_client, NewEmail())).StatusCode);

        await _fixture.UpdateRealmAsync(new { registrationAllowed = false });

        Assert.Equal(HttpStatusCode.Forbidden, (await RegisterAsync(_client, NewEmail())).StatusCode);
    }

    [Fact]
    public async Task Register_RegistrationOn_CreatesUnverifiedUserThatCanLogIn()
    {
        var email = NewEmail();

        var response = await RegisterAsync(_client, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await _fixture.FindUserAsync(email);
        Assert.NotNull(user);
        Assert.False(user.Value.GetProperty("emailVerified").GetBoolean());
        Assert.Empty(user.Value.GetProperty("requiredActions").EnumerateArray());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(await _fixture.GetAccessTokenAsync(email, Password));
        Assert.Equal("false", token.Claims.Single(c => c.Type == "email_verified").Value);
    }

    [Fact]
    public async Task Register_VerifyEmailOn_AddsRequiredActionAndBlocksPasswordLogin()
    {
        await _fixture.UpdateRealmAsync(new { verifyEmail = true });
        var email = NewEmail();

        var response = await RegisterAsync(_client, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await _fixture.FindUserAsync(email);
        Assert.NotNull(user);
        Assert.False(user.Value.GetProperty("emailVerified").GetBoolean());
        Assert.Contains("VERIFY_EMAIL", user.Value.GetProperty("requiredActions").EnumerateArray().Select(a => a.GetString()));
        await Assert.ThrowsAsync<HttpRequestException>(() => _fixture.GetAccessTokenAsync(email, Password));
    }

    [Fact]
    public async Task Register_KeycloakUnreachable_Returns503AndCreatesNoUser()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:Authority", "http://127.0.0.1:1/realms/openksef"));
        using var client = factory.CreateClient();
        var email = NewEmail();

        var response = await RegisterAsync(client, email);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("registration_unavailable", body.GetProperty("code").GetString());
        Assert.Null(await _fixture.FindUserAsync(email));
    }

    [Fact]
    public async Task Register_ServiceAccountWithoutViewRealm_Returns503AndCreatesNoUser()
    {
        await _fixture.RemoveServiceAccountRolesAsync(["view-realm"]);
        var email = NewEmail();

        var response = await RegisterAsync(_client, email);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(await _fixture.FindUserAsync(email));
    }

    [Fact]
    public async Task Register_ServiceAccountWithoutManageUsers_Returns503NotForbidden()
    {
        await _fixture.RemoveServiceAccountRolesAsync(["manage-users"]);

        var response = await RegisterAsync(_client, NewEmail());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SettingsSave_GrantsRegistrationRolesOnce_RegistrationWorks()
    {
        await _fixture.RemoveServiceAccountRolesAsync(TestcontainersFixture.RegistrationRoles);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await RegisterAsync(_client, NewEmail())).StatusCode);

        await SaveSettingsAsync();
        var afterFirst = await _fixture.GetServiceAccountRoleNamesAsync();
        await SaveSettingsAsync();

        Assert.Equal(TestcontainersFixture.RegistrationRoles.Order(), afterFirst.Order());
        Assert.Equal(afterFirst.Order(), (await _fixture.GetServiceAccountRoleNamesAsync()).Order());
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(_client, NewEmail())).StatusCode);
    }

    private async Task SaveSettingsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().UpdateSettingsAsync(
            new SettingsUpdateRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" },
            await _fixture.GetAdminTokenAsync());
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task SettingsSave_WithExternalBaseUrl_KeepsServiceAccountRoles()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
            var result = await settings.UpdateSettingsAsync(
                new SettingsUpdateRequest { KcAdminUsername = "admin", KcAdminPassword = "admin", ExternalBaseUrl = "http://localhost:8080" },
                await _fixture.GetAdminTokenAsync());
            Assert.True(result.Success, result.Error);

            Assert.Superset(
                TestcontainersFixture.RegistrationRoles.ToHashSet(),
                (await _fixture.GetServiceAccountRoleNamesAsync()).ToHashSet());
        }
        finally
        {
            await SetStoredConfigAsync(SystemConfigKeys.ExternalBaseUrl, null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_ClientSecretOnlyInSystemConfig_CreatesUser(bool encrypted)
    {
        var secret = encrypted
            ? SystemConfigKeys.EncryptedPrefix + new AesGcmEncryptionService(Convert.FromBase64String(OpenKSeFWebApplicationFactory.TestEncryptionKey)).Encrypt("test-api-client-secret")
            : "test-api-client-secret";
        await SetStoredConfigAsync(SystemConfigKeys.ApiClientSecret, secret);
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
                builder.UseSetting("Auth:ServiceAccount:ClientSecret", ""));
            using var client = factory.CreateClient();
            var email = NewEmail();

            var response = await RegisterAsync(client, email);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(await _fixture.FindUserAsync(email));
        }
        finally
        {
            await SetStoredConfigAsync(SystemConfigKeys.ApiClientSecret, null);
        }
    }

    [Fact]
    public async Task Register_BlankSecret_MakesNoKeycloakCall()
    {
        var calls = new List<Uri?>();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:ServiceAccount:ClientSecret", "");
            builder.ConfigureTestServices(services => services.AddHttpClient("keycloak")
                .AddHttpMessageHandler(() => new RecordingHandler(calls)));
        });
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, NewEmail());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Register_RegistrationOffWithoutSecret_Returns403AndMakesNoKeycloakCall()
    {
        await SetStoredConfigAsync(SystemConfigKeys.RegistrationAllowed, "false");
        try
        {
            var calls = new List<Uri?>();
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Auth:ServiceAccount:ClientSecret", "");
                builder.ConfigureTestServices(services => services.AddHttpClient("keycloak")
                    .AddHttpMessageHandler(() => new RecordingHandler(calls)));
            });
            using var client = factory.CreateClient();

            var response = await RegisterAsync(client, NewEmail());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("registration_disabled", body.GetProperty("code").GetString());
            Assert.Empty(calls);
        }
        finally
        {
            await SetStoredConfigAsync(SystemConfigKeys.RegistrationAllowed, null);
        }
    }

    [Fact]
    public async Task Register_OverPerIpLimit_Returns429WithReadableBody()
    {
        await _fixture.UpdateRealmAsync(new { registrationAllowed = false });

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await RegisterAsync(_client, NewEmail())).StatusCode);

        var response = await RegisterAsync(_client, NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    private sealed class RecordingHandler(List<Uri?> calls) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls.Add(request.RequestUri);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static string NewEmail() => $"register-{Guid.NewGuid():N}@openksef.test";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/account/register",
            new { email, password = Password, firstName = "Nowy", lastName = "Klient" });

    private async Task SetStoredConfigAsync(string key, string? value)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.SystemConfigs.FindAsync(key);
        if (row is not null)
            db.SystemConfigs.Remove(row);
        if (value is not null)
            db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
