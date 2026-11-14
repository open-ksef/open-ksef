using System.Net.Http.Json;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

/// <summary>A second realm on the fixture Keycloak, registered in openksef as the OIDC identity provider "google".</summary>
public sealed class FakeGoogle : IAsyncDisposable
{
    public const string Password = "Google1234!";
    private const string Realm = "fakegoogle";

    private readonly TestcontainersFixture _fixture;

    private FakeGoogle(TestcontainersFixture fixture) => _fixture = fixture;

    private string RealmUrl => $"{_fixture.KeycloakBaseUrl}/admin/realms/{Realm}";

    public static async Task<FakeGoogle> CreateAsync(TestcontainersFixture fixture)
    {
        var google = new FakeGoogle(fixture);
        using var http = await fixture.CreateAdminClientAsync();
        (await http.PostAsJsonAsync($"{fixture.KeycloakBaseUrl}/admin/realms", new { realm = Realm, enabled = true, sslRequired = "none" })).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"{google.RealmUrl}/clients", new
        {
            clientId = "gclient", publicClient = false, secret = "gsecret", standardFlowEnabled = true, redirectUris = new[] { "*" },
        })).EnsureSuccessStatusCode();
        // Keycloak calls the token endpoint itself, so that URL is the in-container one; the browser gets the published port.
        (await http.PostAsJsonAsync($"{fixture.AdminRealmUrl}/identity-provider/instances", new
        {
            alias = "google",
            providerId = "oidc",
            enabled = true,
            trustEmail = true,
            config = new Dictionary<string, string>
            {
                ["authorizationUrl"] = $"{fixture.KeycloakBaseUrl}/realms/{Realm}/protocol/openid-connect/auth",
                ["tokenUrl"] = $"http://localhost:8080/realms/{Realm}/protocol/openid-connect/token",
                ["disableUserInfo"] = "true",
                ["clientId"] = "gclient",
                ["clientSecret"] = "gsecret",
                ["clientAuthMethod"] = "client_secret_post",
                ["defaultScope"] = "openid email profile",
                ["syncMode"] = "IMPORT",
                ["validateSignature"] = "false",
            },
        })).EnsureSuccessStatusCode();
        return google;
    }

    public async Task<(string Id, string Email)> CreateUserAsync(string email)
    {
        using var http = await _fixture.CreateAdminClientAsync();
        var response = await http.PostAsJsonAsync($"{RealmUrl}/users", new
        {
            username = email, email, emailVerified = true, firstName = "Google", lastName = "User", enabled = true,
            credentials = new[] { new { type = "password", value = Password, temporary = false } },
        });
        response.EnsureSuccessStatusCode();
        return (response.Headers.Location!.Segments[^1], email);
    }

    public async ValueTask DisposeAsync()
    {
        using var http = await _fixture.CreateAdminClientAsync();
        await http.DeleteAsync($"{_fixture.AdminRealmUrl}/identity-provider/instances/google");
        await http.DeleteAsync(RealmUrl);
    }
}
