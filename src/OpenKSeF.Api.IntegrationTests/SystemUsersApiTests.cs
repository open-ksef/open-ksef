using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class SystemUsersApiTests : IAsyncLifetime
{
    private const string Password = "Test1234!";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SystemUsersApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _fixture.GrantServiceAccountRolesAsync(TestcontainersFixture.RegistrationRoles);
        await _fixture.SetApiAdminScopeAsync(true);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().UpdateSettingsAsync(
            new SettingsUpdateRequest { KcAdminUsername = "admin", KcAdminPassword = "admin" },
            await _fixture.GetAdminTokenAsync());
        Assert.True(result.Success, result.Error);
    }

    public async Task DisposeAsync()
    {
        await _fixture.RemoveServiceAccountRolesAsync(["view-users"]);
        await _fixture.GrantServiceAccountRolesAsync(TestcontainersFixture.RegistrationRoles);
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Reset_EnrolledUser_Returns204_RemovesOtpAndCodes_WritesOneAuditRow_ForcesSetupAgain()
    {
        var (email, userId) = await EnrolledUserAsync();
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var lookup = await _client.GetFromJsonAsync<JsonElement>($"/api/system/users/two-factor?email={Uri.EscapeDataString(email)}");
        Assert.True(lookup.GetProperty("resetAvailable").GetBoolean());
        Assert.Equal(userId, lookup.GetProperty("userId").GetString());
        Assert.Equal(email, lookup.GetProperty("email").GetString());
        Assert.Equal("enabled", lookup.GetProperty("status").GetString());
        Assert.Equal(12, lookup.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, lookup.GetProperty("enabledAt").ValueKind);

        var reset = await _client.PostAsync($"/api/system/users/{userId}/two-factor/reset", null);
        var again = await _client.PostAsync($"/api/system/users/{userId}/two-factor/reset", null);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.DoesNotContain(await _fixture.GetUserCredentialsAsync(userId), c => c.GetProperty("type").GetString() is "otp" or "recovery-authn-codes");
        var audit = Assert.Single(await AuditRowsAsync(userId));
        Assert.Equal(AuditEvent.TwoFactorReset, audit.Action);
        Assert.Equal((await _fixture.FindUserAsync("testuser@openksef.test"))!.Value.GetProperty("id").GetString(), audit.ActorUserId);
        Assert.Equal("testuser@openksef.test", audit.ActorEmail);
        Assert.Equal(email, audit.TargetEmail);

        var notSet = await _client.GetFromJsonAsync<JsonElement>($"/api/system/users/two-factor?email={Uri.EscapeDataString(email)}");
        Assert.Equal("notSet", notSet.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, notSet.GetProperty("recoveryCodesLeft").ValueKind);
        using var browser = new KeycloakBrowser();
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        Assert.True(browser.OnPage("kc-totp-settings-form"), browser.Html);
    }

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var lookup = await _client.GetAsync("/api/system/users/two-factor?email=nobody-t726%40openksef.test");
        var reset = await _client.PostAsync($"/api/system/users/{Guid.NewGuid()}/two-factor/reset", null);

        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal("user_not_found", (await lookup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        Assert.Equal("user_not_found", (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task NonAdmin_Returns403_AndRemovesNothing()
    {
        var (email, userId) = await EnrolledUserAsync();
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync("plainuser"));

        var lookup = await _client.GetAsync($"/api/system/users/two-factor?email={Uri.EscapeDataString(email)}");
        var reset = await _client.PostAsync($"/api/system/users/{userId}/two-factor/reset", null);

        Assert.Equal(HttpStatusCode.Forbidden, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Contains(await _fixture.GetUserCredentialsAsync(userId), c => c.GetProperty("type").GetString() == "otp");
        Assert.Empty(await AuditRowsAsync(userId));
    }

    [Fact]
    public async Task ServiceAccountWithoutManageUsers_ResetReturns409_LookupSaysUnavailable_NoRoleGranted()
    {
        var (email, userId) = await EnrolledUserAsync();
        await _fixture.RemoveServiceAccountRolesAsync(["manage-users"]);
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());

        var reset = await _client.PostAsync($"/api/system/users/{userId}/two-factor/reset", null);
        var blind = await _client.GetFromJsonAsync<JsonElement>($"/api/system/users/two-factor?email={Uri.EscapeDataString(email)}");
        await _fixture.GrantServiceAccountRolesAsync(["view-users"]);
        var viewOnly = await _client.GetFromJsonAsync<JsonElement>($"/api/system/users/two-factor?email={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        Assert.Equal("reset_unavailable", (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.False(blind.GetProperty("resetAvailable").GetBoolean());
        Assert.Equal(TwoFactorUserResponse.ManageUsersMissing, blind.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, blind.GetProperty("userId").ValueKind);
        Assert.False(viewOnly.GetProperty("resetAvailable").GetBoolean());
        Assert.Equal(TwoFactorUserResponse.ManageUsersMissing, viewOnly.GetProperty("reason").GetString());
        Assert.Equal(userId, viewOnly.GetProperty("userId").GetString());
        Assert.Equal("enabled", viewOnly.GetProperty("status").GetString());
        Assert.Contains(await _fixture.GetUserCredentialsAsync(userId), c => c.GetProperty("type").GetString() == "otp");
        Assert.Empty(await AuditRowsAsync(userId));
        Assert.DoesNotContain("manage-users", await _fixture.GetServiceAccountRoleNamesAsync());
    }

    private async Task<List<AuditEvent>> AuditRowsAsync(string targetUserId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEvents
            .Where(a => a.TargetUserId == targetUserId).ToListAsync();
    }

    private async Task<(string Email, string UserId)> EnrolledUserAsync()
    {
        var email = $"t726-{Guid.NewGuid():N}@openksef.test";
        var userId = await _fixture.CreateUserAsync(email, Password);
        using var browser = new KeycloakBrowser();
        await browser.GetAsync(KeycloakBrowser.AuthUrl(_fixture.KeycloakAuthority));
        await browser.SubmitLoginAsync(email, Password);
        await browser.EnrolAsync();
        Assert.False(string.IsNullOrEmpty(browser.Code));
        return (email, userId);
    }
}
