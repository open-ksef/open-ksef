using System.Net.Http.Json;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class TestcontainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("openksef")
        .WithUsername("openksef")
        .WithPassword("openksef_test")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder("quay.io/keycloak/keycloak:26.0")
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "realm-test.json"),
            "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .Build();

    public string PostgresConnectionString => Postgres.GetConnectionString();

    public string KeycloakAuthority =>
        $"{Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/realms/openksef";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            Postgres.StartAsync(),
            Keycloak.StartAsync());

        await WaitForKeycloakRealmAsync();
    }

    private async Task WaitForKeycloakRealmAsync(int maxRetries = 60, int delayMs = 2000)
    {
        using var http = new HttpClient();
        var realmUrl = $"{Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/realms/openksef";

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await http.GetAsync(realmUrl);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"Keycloak realm 'openksef' not available after {maxRetries * delayMs / 1000}s at {realmUrl}");
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            Postgres.DisposeAsync().AsTask(),
            Keycloak.DisposeAsync().AsTask());
    }

    public async Task<string> GetAccessTokenAsync(
        string username = "testuser",
        string password = "Test1234!",
        string clientId = "openksef-mobile")
    {
        using var http = new HttpClient();
        var tokenEndpoint = $"{KeycloakAuthority}/protocol/openid-connect/token";

        var response = await http.PostAsync(tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = username,
                ["password"] = password,
            }));

        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content
            .ReadFromJsonAsync<TokenResponse>();

        return tokenResponse?.AccessToken
            ?? throw new InvalidOperationException("Token response did not contain access_token");
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
        string AccessToken);
}

[CollectionDefinition(Name)]
public class TestcontainersCollection : ICollectionFixture<TestcontainersFixture>
{
    public const string Name = "Testcontainers";
}
