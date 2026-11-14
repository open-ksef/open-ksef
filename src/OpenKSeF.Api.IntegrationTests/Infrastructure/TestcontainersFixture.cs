using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class TestcontainersFixture : IAsyncLifetime
{
    public static readonly string[] RegistrationRoles = ["manage-users", "view-realm"];

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("openksef")
        .WithUsername("openksef")
        .WithPassword("openksef_test")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.2@sha256:c68e286cacd87b942ac1939a0a1481461109d6acd3a57dc496969a319a76bbf0")
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

    public async Task<string> GetAdminTokenAsync()
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = "admin",
            }));

        response.EnsureSuccessStatusCode();
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return tokenResponse!.AccessToken;
    }

    public async Task UpdateRealmAsync(object settings)
    {
        using var http = await CreateAdminClientAsync();
        (await http.PutAsJsonAsync(AdminRealmUrl, settings)).EnsureSuccessStatusCode();
    }

    public async Task<JsonElement?> FindUserAsync(string email)
    {
        using var http = await CreateAdminClientAsync();
        var users = await http.GetFromJsonAsync<JsonElement[]>(
            $"{AdminRealmUrl}/users?email={Uri.EscapeDataString(email)}&exact=true");
        return users!.Length > 0 ? users[0] : null;
    }

    public async Task GrantServiceAccountRolesAsync(IReadOnlyCollection<string> names)
    {
        using var http = await CreateAdminClientAsync();
        var (mappingsUrl, _) = await GetServiceAccountRoleMappingsUrlAsync(http);
        var available = await http.GetFromJsonAsync<JsonElement[]>($"{mappingsUrl}/available");
        var roles = available!.Where(r => names.Contains(r.GetProperty("name").GetString()!)).ToArray();
        if (roles.Length > 0)
            (await http.PostAsJsonAsync(mappingsUrl, roles)).EnsureSuccessStatusCode();
    }

    public async Task RemoveServiceAccountRolesAsync(IReadOnlyCollection<string> names)
    {
        using var http = await CreateAdminClientAsync();
        var (mappingsUrl, mapped) = await GetServiceAccountRoleMappingsUrlAsync(http);
        var roles = mapped.Where(r => names.Contains(r.GetProperty("name").GetString()!)).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Delete, mappingsUrl) { Content = JsonContent.Create(roles) };
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }

    public async Task SetApiAdminScopeAsync(bool assigned)
    {
        using var http = await CreateAdminClientAsync();
        var apiClient = (await http.GetFromJsonAsync<JsonElement[]>($"{AdminRealmUrl}/clients?clientId=openksef-api"))!.Single();
        var scope = (await http.GetFromJsonAsync<JsonElement[]>($"{AdminRealmUrl}/client-scopes"))!
            .Single(s => s.GetProperty("name").GetString() == "kc-admin-api");
        var url = $"{AdminRealmUrl}/clients/{apiClient.GetProperty("id").GetString()}/default-client-scopes/{scope.GetProperty("id").GetString()}";
        (assigned ? await http.PutAsync(url, null) : await http.DeleteAsync(url)).EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyCollection<string>> GetServiceAccountRoleNamesAsync()
    {
        using var http = await CreateAdminClientAsync();
        var (_, mapped) = await GetServiceAccountRoleMappingsUrlAsync(http);
        return mapped.Select(r => r.GetProperty("name").GetString()!).ToArray();
    }

    public async Task<JsonElement[]> GetApiAudienceMappersAsync(string clientId)
    {
        using var http = await CreateAdminClientAsync();
        var mappers = await http.GetFromJsonAsync<JsonElement[]>(await ProtocolMappersUrlAsync(http, clientId));
        return mappers!.Where(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"
            && m.GetProperty("config") is var config
            && (config.TryGetProperty("included.client.audience", out var audience) && audience.GetString() == "openksef-api"
                || config.TryGetProperty("included.custom.audience", out var custom) && custom.GetString() == "openksef-api")).ToArray();
    }

    public async Task AddAudienceMapperAsync(string clientId, string name, Dictionary<string, string> config)
    {
        using var http = await CreateAdminClientAsync();
        (await http.PostAsJsonAsync(await ProtocolMappersUrlAsync(http, clientId), new
        {
            name,
            protocol = "openid-connect",
            protocolMapper = "oidc-audience-mapper",
            config,
        })).EnsureSuccessStatusCode();
    }

    public async Task DeleteApiAudienceMappersAsync(string clientId)
    {
        var mappers = await GetApiAudienceMappersAsync(clientId);
        using var http = await CreateAdminClientAsync();
        var url = await ProtocolMappersUrlAsync(http, clientId);
        foreach (var mapper in mappers)
            (await http.DeleteAsync($"{url}/{mapper.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
    }

    public async Task DeleteRealmAsync()
    {
        using var http = await CreateAdminClientAsync();
        (await http.DeleteAsync(AdminRealmUrl)).EnsureSuccessStatusCode();
    }

    public async Task RestoreTestRealmAsync()
    {
        using var http = await CreateAdminClientAsync();
        await http.DeleteAsync(AdminRealmUrl);
        using var content = new StringContent(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "realm-test.json")),
            System.Text.Encoding.UTF8, "application/json");
        (await http.PostAsync($"{Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/admin/realms", content)).EnsureSuccessStatusCode();
    }

    public static string[] AccessTokenAudiences(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var json = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '='))).RootElement;
        if (!json.TryGetProperty("aud", out var aud))
            return [];
        return aud.ValueKind == JsonValueKind.Array ? aud.EnumerateArray().Select(a => a.GetString()!).ToArray() : [aud.GetString()!];
    }

    private async Task<string> ProtocolMappersUrlAsync(HttpClient http, string clientId)
    {
        var client = (await http.GetFromJsonAsync<JsonElement[]>($"{AdminRealmUrl}/clients?clientId={clientId}"))!.Single();
        return $"{AdminRealmUrl}/clients/{client.GetProperty("id").GetString()}/protocol-mappers/models";
    }

    private string AdminRealmUrl => $"{Keycloak.GetBaseAddress().ToString().TrimEnd('/')}/admin/realms/openksef";

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());
        return http;
    }

    private async Task<(string Url, JsonElement[] Mapped)> GetServiceAccountRoleMappingsUrlAsync(HttpClient http)
    {
        var apiClient = (await http.GetFromJsonAsync<JsonElement[]>($"{AdminRealmUrl}/clients?clientId=openksef-api"))!.Single();
        var realmManagement = (await http.GetFromJsonAsync<JsonElement[]>($"{AdminRealmUrl}/clients?clientId=realm-management"))!.Single();
        var serviceAccount = await http.GetFromJsonAsync<JsonElement>(
            $"{AdminRealmUrl}/clients/{apiClient.GetProperty("id").GetString()}/service-account-user");
        var url = $"{AdminRealmUrl}/users/{serviceAccount.GetProperty("id").GetString()}/role-mappings/clients/{realmManagement.GetProperty("id").GetString()}";
        return (url, (await http.GetFromJsonAsync<JsonElement[]>(url))!);
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
