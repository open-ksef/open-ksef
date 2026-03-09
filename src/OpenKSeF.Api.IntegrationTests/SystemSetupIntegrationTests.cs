using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;

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

        // 1. Authenticate
        var authRequest = new SetupAuthenticateRequest("admin", "admin");
        var authResp = await _client.PostAsJsonAsync("/api/system/setup/authenticate", authRequest);
        authResp.EnsureSuccessStatusCode();
        var authResult = await authResp.Content.ReadFromJsonAsync<SetupAuthenticateResponse>();

        // 2. Apply setup
        var applyRequest = new SetupApplyRequest
        {
            ExternalBaseUrl = "http://localhost:8080",
            KSeFBaseUrl = "https://ksef-test.mf.gov.pl/api",
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

        var applyResult = await applyResp.Content.ReadFromJsonAsync<SetupApplyResponse>();
        Assert.NotNull(applyResult);
        Assert.True(applyResult!.Success);
        Assert.False(string.IsNullOrEmpty(applyResult.EncryptionKey));
        Assert.False(string.IsNullOrEmpty(applyResult.ApiClientSecret));

        // 3. Verify setup-status now returns initialized
        var statusResp = await _client.GetAsync("/api/system/setup-status");
        var status = await statusResp.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.True(status!.IsInitialized);

        // 4. Verify DB has the config
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encKey = await db.SystemConfigs.FindAsync("encryption_key");
        Assert.NotNull(encKey);
        Assert.False(string.IsNullOrEmpty(encKey!.Value));
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
            KSeFBaseUrl = "https://ksef-test.mf.gov.pl/api",
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
