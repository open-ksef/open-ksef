using System.Net;
using System.Text.Json;

namespace OpenKSeF.Api.Services;

public interface IKeycloakUserService
{
    Task<CreateUserResult> CreateUserAsync(string email, string password, string? firstName, string? lastName);
}

public record CreateUserResult(bool Success, string? ErrorMessage, HttpStatusCode StatusCode);

public class KeycloakUserService : IKeycloakUserService
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenEndpoint;
    private readonly string _adminUsersEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ILogger<KeycloakUserService> _logger;

    public KeycloakUserService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<KeycloakUserService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("keycloak");
        _logger = logger;

        var authority = configuration["Auth:Authority"]?.TrimEnd('/') ?? "";
        _tokenEndpoint = $"{authority}/protocol/openid-connect/token";

        // Authority: http://keycloak:8080/auth/realms/openksef
        // Admin API: http://keycloak:8080/auth/admin/realms/openksef/users
        var realmsIndex = authority.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (realmsIndex >= 0)
        {
            var baseUrl = authority[..realmsIndex];
            var realmName = authority[(realmsIndex + "/realms/".Length)..];
            _adminUsersEndpoint = $"{baseUrl}/admin/realms/{realmName}/users";
        }
        else
        {
            _adminUsersEndpoint = $"{authority}/admin/realms/openksef/users";
        }

        _clientId = configuration["Auth:ServiceAccount:ClientId"] ?? "openksef-api";
        _clientSecret = configuration["Auth:ServiceAccount:ClientSecret"] ?? "";
    }

    public async Task<CreateUserResult> CreateUserAsync(
        string email, string password, string? firstName, string? lastName)
    {
        if (string.IsNullOrEmpty(_clientSecret))
        {
            _logger.LogWarning(
                "Auth:ServiceAccount:ClientSecret not configured — user registration disabled.");
            return new CreateUserResult(false, "Registration service not configured.", HttpStatusCode.ServiceUnavailable);
        }

        try
        {
            var adminToken = await GetServiceAccountTokenAsync();
            if (adminToken is null)
                return new CreateUserResult(false, "Failed to authenticate with identity provider.", HttpStatusCode.ServiceUnavailable);

            var userPayload = new
            {
                username = email,
                email,
                firstName = firstName ?? "",
                lastName = lastName ?? "",
                enabled = true,
                emailVerified = true,
                credentials = new[]
                {
                    new { type = "password", value = password, temporary = false }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _adminUsersEndpoint)
            {
                Content = JsonContent.Create(userPayload)
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new CreateUserResult(false, "An account with this email already exists.", HttpStatusCode.Conflict);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Keycloak user creation failed: {StatusCode} {Body}",
                    response.StatusCode, errorBody);
                return new CreateUserResult(false, "Registration failed. Please try again.", response.StatusCode);
            }

            return new CreateUserResult(true, null, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User creation failed for email {Email}", email);
            return new CreateUserResult(false, "An unexpected error occurred.", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<string?> GetServiceAccountTokenAsync()
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret
        };

        var response = await _httpClient.PostAsync(_tokenEndpoint, new FormUrlEncodedContent(formData));
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Service account token request failed: {StatusCode} {Body}",
                response.StatusCode, errorBody);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString();
    }
}
