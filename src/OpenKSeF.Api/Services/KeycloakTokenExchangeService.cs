using System.Text.Json;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

public interface IKeycloakTokenExchangeService
{
    Task<TokenExchangeResult?> ExchangeSetupTokenAsync(string userId);
}

public record TokenExchangeResult(string AccessToken, string RefreshToken, int ExpiresIn);

public class KeycloakTokenExchangeService : IKeycloakTokenExchangeService
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _configClientSecret;
    private readonly ISystemConfigService? _systemConfig;
    private readonly ILogger<KeycloakTokenExchangeService> _logger;

    public KeycloakTokenExchangeService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<KeycloakTokenExchangeService> logger,
        ISystemConfigService? systemConfig = null)
    {
        _httpClient = httpClientFactory.CreateClient("keycloak");
        _logger = logger;
        _systemConfig = systemConfig;

        var authority = configuration["Auth:Authority"]?.TrimEnd('/') ?? "";
        _tokenEndpoint = $"{authority}/protocol/openid-connect/token";
        _clientId = configuration["Auth:ServiceAccount:ClientId"] ?? "openksef-api";
        _configClientSecret = configuration["Auth:ServiceAccount:ClientSecret"] ?? "";
    }

    private string ResolveClientSecret()
    {
        var dbSecret = _systemConfig?.GetValue(SystemConfigKeys.ApiClientSecret);
        if (!string.IsNullOrEmpty(dbSecret))
            return dbSecret;
        return _configClientSecret;
    }

    public async Task<TokenExchangeResult?> ExchangeSetupTokenAsync(string userId)
    {
        var clientSecret = ResolveClientSecret();
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogWarning(
                "Auth:ServiceAccount:ClientSecret not configured — token exchange disabled. " +
                "Configure the openksef-api client with service account enabled in Keycloak.");
            return null;
        }

        try
        {
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["client_id"] = _clientId,
                ["client_secret"] = clientSecret,
                ["requested_subject"] = userId,
                ["requested_token_type"] = "urn:ietf:params:oauth:token-type:refresh_token",
                ["audience"] = "openksef-api",
                ["scope"] = "openid profile email"
            };

            var response = await _httpClient.PostAsync(
                _tokenEndpoint,
                new FormUrlEncodedContent(formData));

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Keycloak token exchange failed: {StatusCode} {Body}",
                    response.StatusCode, errorBody);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return new TokenExchangeResult(
                AccessToken: json.GetProperty("access_token").GetString()!,
                RefreshToken: json.GetProperty("refresh_token").GetString()!,
                ExpiresIn: json.GetProperty("expires_in").GetInt32());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token exchange failed for user {UserId}", userId);
            return null;
        }
    }
}
