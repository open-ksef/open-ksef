using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class SystemSetupIntegrationTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SystemSetupIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task SetupStatus_FreshSystem_ReturnsNotInitialized()
    {
        await ClearSystemConfigAsync();

        var response = await _client.GetAsync("/api/system/setup-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.NotNull(status);
        Assert.False(status!.IsInitialized);
    }

    [Fact]
    public async Task Authenticate_ValidKeycloakAdmin_ReturnsSetupToken()
    {
        await ClearSystemConfigAsync();

        var request = new SetupAuthenticateRequest("admin", "admin");
        var response = await _client.PostAsJsonAsync("/api/system/setup/authenticate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.SetupToken));
        Assert.Equal(600, result.ExpiresInSeconds);
    }

    [Fact]
    public async Task Authenticate_InvalidKeycloakCreds_ReturnsBadRequest()
    {
        await ClearSystemConfigAsync();

        var request = new SetupAuthenticateRequest("admin", "wrong-password");
        var response = await _client.PostAsJsonAsync("/api/system/setup/authenticate", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullSetupFlow_PersistsConfigInDb()
    {
        await ClearSystemConfigAsync();
        await _fixture.RemoveServiceAccountRolesAsync(TestcontainersFixture.RegistrationRoles);

        // 1. Authenticate
        var authRequest = new SetupAuthenticateRequest("admin", "admin");
        var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", authRequest);
        authResp.EnsureSuccessStatusCode();
        var authResult = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

        // 2. Apply setup
        var applyRequest = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "integration-test@open-ksef.pl",
            AdminPassword = "IntegrationTest1234!",
            RegistrationAllowed = true,
            VerifyEmail = false,
            LoginWithEmailAllowed = true,
            ResetPasswordAllowed = false,
        };

        using var applyMsg = new HttpRequestMessage(HttpMethod.Post, "/api/system/setup/apply");
        applyMsg.Headers.Add("X-Setup-Token", authResult!.SetupToken);
        applyMsg.Content = JsonContent.Create(applyRequest);

        var applyResp = await _client.SendAsync(applyMsg);
        applyResp.EnsureSuccessStatusCode();

        var applyJson = await applyResp.Content.ReadAsStringAsync();
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<SetupApplyResponse>(applyJson, System.Text.Json.JsonSerializerOptions.Web)!.Success);
        Assert.DoesNotContain("secret", applyJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encryptionKey", applyJson, StringComparison.OrdinalIgnoreCase);

        // 3. Verify setup-status now returns initialized
        var statusResp = await _client.GetAsync("/api/system/setup-status");
        var status = await statusResp.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.True(status!.IsInitialized);

        // 4. Verify DB has the config
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.SystemConfigs.FindAsync("encryption_key"));
        Assert.NotNull(await db.SystemConfigs.FindAsync("api_client_secret"));

        Assert.Empty((await _fixture.GetServiceAccountRoleNamesAsync()).Intersect(TestcontainersFixture.RegistrationRoles));

        using var settings = new HttpRequestMessage(HttpMethod.Post, "/api/system/settings")
        {
            Content = JsonContent.Create(new { kcAdminUsername = "admin", kcAdminPassword = "admin" }),
        };
        settings.Headers.Authorization = new("Bearer", await _fixture.GetAccessTokenAsync(applyRequest.AdminEmail, applyRequest.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(settings)).StatusCode);
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("https://api.ksef.mf.gov.pl/v2")]
    [InlineData("https://ksef.podatki.gov.pl/api")]
    public async Task Apply_ProdOrLegacyProductionUrl_StoresTheCanonicalKey(string environment)
    {
        await ClearSystemConfigAsync();
        try
        {
            var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", new SetupAuthenticateRequest("admin", "admin"));
            authResp.EnsureSuccessStatusCode();
            var auth = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

            using var applyMsg = new HttpRequestMessage(HttpMethod.Post, "/api/system/setup/apply");
            applyMsg.Headers.Add("X-Setup-Token", auth!.SetupToken);
            applyMsg.Content = JsonContent.Create(new SetupApplyRequest
            {
                ExternalBaseUrl = "http://localhost:8080",
                KSeFBaseUrl = environment,
                AdminEmail = $"canonical-{Guid.NewGuid():N}@open-ksef.pl",
                AdminPassword = "Test1234!",
            });

            var response = await _client.SendAsync(applyMsg);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("production", (await db.SystemConfigs.FindAsync(SystemConfigKeys.KSeFEnvironment))!.Value);
            Assert.Null(await db.SystemConfigs.FindAsync(SystemConfigKeys.KSeFBaseUrl));
        }
        finally
        {
            await ClearSystemConfigAsync();
        }
    }

    [Fact]
    public async Task Apply_UnknownEnvironment_Is400_NotInitialized()
    {
        await ClearSystemConfigAsync();
        var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", new SetupAuthenticateRequest("admin", "admin"));
        authResp.EnsureSuccessStatusCode();
        var auth = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

        using var applyMsg = new HttpRequestMessage(HttpMethod.Post, "/api/system/setup/apply");
        applyMsg.Headers.Add("X-Setup-Token", auth!.SetupToken);
        applyMsg.Content = JsonContent.Create(new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "tset",
            AdminEmail = "unknown-environment@open-ksef.pl",
            AdminPassword = "Test1234!",
        });

        var response = await _client.SendAsync(applyMsg);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown KSeF environment", (await response.Content.ReadFromJsonAsync<SetupApplyResponse>())!.Error);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.SystemConfigs.ToListAsync());
    }

    [Fact]
    public async Task Apply_InvalidFirstTenantNip_Is400_NothingCreated()
    {
        await ClearSystemConfigAsync();
        var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", new SetupAuthenticateRequest("admin", "admin"));
        authResp.EnsureSuccessStatusCode();
        var auth = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

        using var applyMsg = new HttpRequestMessage(HttpMethod.Post, "/api/system/setup/apply");
        applyMsg.Headers.Add("X-Setup-Token", auth!.SetupToken);
        applyMsg.Content = JsonContent.Create(new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "invalid-nip@open-ksef.pl",
            AdminPassword = "Test1234!",
            FirstTenantNip = "1234567890",
        });

        var response = await _client.SendAsync(applyMsg);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Niepoprawny NIP", (await response.Content.ReadFromJsonAsync<SetupApplyResponse>())!.Error);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.SystemConfigs.ToListAsync());
        Assert.False(await db.Tenants.AnyAsync(t => t.Nip == "1234567890"));
    }

    [Fact]
    public async Task HostStart_LegacyKeyRow_IsAdoptedBeforeTheCacheLoads()
    {
        await ClearSystemConfigAsync();
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var tenantId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SystemConfigs.Add(new SystemConfig { Key = "encryption_key", Value = Convert.ToBase64String(oldKey), UpdatedAt = DateTime.UtcNow });
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "upgrade-user", Nip = "1234563218", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.KSeFCredentials.Add(new KSeFCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EncryptedToken = new AesGcmEncryptionService(oldKey).Encrypt("ksef-token"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var upgraded = new OpenKSeFWebApplicationFactory(_fixture);
        using (var scope = upgraded.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Null(await db.SystemConfigs.FindAsync("encryption_key"));
            Assert.Null(scope.ServiceProvider.GetRequiredService<ISystemConfigService>().GetValue(SystemConfigKeys.EncryptionKey));

            var credential = await db.KSeFCredentials.SingleAsync(c => c.TenantId == tenantId);
            Assert.Equal("ksef-token", scope.ServiceProvider.GetRequiredService<IEncryptionService>().Decrypt(credential.EncryptedToken!));

            db.Tenants.Remove(await db.Tenants.SingleAsync(t => t.Id == tenantId));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Apply_AfterInitialized_Returns403()
    {
        await ClearSystemConfigAsync();

        // First: complete setup
        var authRequest = new SetupAuthenticateRequest("admin", "admin");
        var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", authRequest);
        authResp.EnsureSuccessStatusCode();
        var authResult = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

        var applyRequest = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "test",
            AdminEmail = "block-test@open-ksef.pl",
            AdminPassword = "Test1234!",
        };

        using var applyMsg = new HttpRequestMessage(HttpMethod.Post, "/api/system/setup/apply");
        applyMsg.Headers.Add("X-Setup-Token", authResult!.SetupToken);
        applyMsg.Content = JsonContent.Create(applyRequest);
        var firstResp = await _client.SendAsync(applyMsg);
        firstResp.EnsureSuccessStatusCode();

        // Second attempt: should be blocked
        var authResp2 = await _client.PostAsJsonAsync("/api/system/setup/authenticate", authRequest);
        Assert.Equal(HttpStatusCode.Forbidden, authResp2.StatusCode);
    }

    private async Task ClearSystemConfigAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SystemConfigs.RemoveRange(db.SystemConfigs);
        await db.SaveChangesAsync();

        var configService = scope.ServiceProvider.GetRequiredService<Domain.Services.ISystemConfigService>();
        await configService.RefreshCacheAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
