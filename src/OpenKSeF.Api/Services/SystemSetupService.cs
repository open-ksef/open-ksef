using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

public interface ISystemSetupService
{
    Task<string?> AuthenticateAdminAsync(string username, string password);
    Task<SetupApplyResponse> ApplySetupAsync(SetupApplyRequest request, string adminToken, CancellationToken ct = default);
}

public sealed class SystemSetupService : ISystemSetupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ISystemConfigService _systemConfig;
    private readonly ILogger<SystemSetupService> _logger;

    public SystemSetupService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ISystemConfigService systemConfig,
        ILogger<SystemSetupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _systemConfig = systemConfig;
        _logger = logger;
    }

    private string KeycloakInternalBaseUrl
    {
        get
        {
            var authority = _configuration["Auth:Authority"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(authority))
                return "http://keycloak:8080/auth";
            var idx = authority.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? authority[..idx] : authority;
        }
    }

    public async Task<string?> AuthenticateAdminAsync(string username, string password)
    {
        var client = _httpClientFactory.CreateClient("keycloak");
        var tokenUrl = $"{KeycloakInternalBaseUrl}/realms/master/protocol/openid-connect/token";

        var form = new Dictionary<string, string>
        {
            ["client_id"] = "admin-cli",
            ["username"] = username,
            ["password"] = password,
            ["grant_type"] = "password",
        };

        try
        {
            var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(form));
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Keycloak admin auth failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate Keycloak admin");
            return null;
        }
    }

    public async Task<SetupApplyResponse> ApplySetupAsync(
        SetupApplyRequest request, string adminToken, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("keycloak");
            var kcBase = KeycloakInternalBaseUrl;
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {adminToken}",
            };

            // 1. Generate encryption key
            var encryptionKeyBytes = RandomNumberGenerator.GetBytes(32);
            var encryptionKey = Convert.ToBase64String(encryptionKeyBytes);
            _logger.LogInformation("Generated encryption key");

            // 2. Fetch openksef-api client secret
            var apiClientSecret = await FetchApiClientSecretAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Fetched API client secret");

            // 3. Enable token-exchange role
            await EnableTokenExchangeAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Token exchange configured");

            // 4. Update Keycloak client redirect URIs
            await UpdateClientRedirectUrisAsync(client, kcBase, adminToken, request.ExternalBaseUrl, ct);
            _logger.LogInformation("Updated client redirect URIs");

            // 5. Configure Keycloak realm (login policy, password policy, SMTP)
            await ConfigureRealmAsync(client, kcBase, adminToken, request, ct);
            _logger.LogInformation("Configured Keycloak realm settings");

            // 6. Create first admin user
            await CreateAdminUserAsync(client, kcBase, adminToken, request, ct);
            _logger.LogInformation("Created admin user");

            // 7. Configure Google IdP (if provided)
            if (!string.IsNullOrEmpty(request.GoogleClientId) && !string.IsNullOrEmpty(request.GoogleClientSecret))
            {
                await ConfigureGoogleIdpAsync(client, kcBase, adminToken, request, ct);
                _logger.LogInformation("Configured Google IdP");
            }

            // 8. Store config in DB
            var configValues = new Dictionary<string, string>
            {
                [SystemConfigKeys.EncryptionKey] = encryptionKey,
                [SystemConfigKeys.ApiClientSecret] = apiClientSecret,
                [SystemConfigKeys.ExternalBaseUrl] = request.ExternalBaseUrl.TrimEnd('/'),
                [SystemConfigKeys.KSeFBaseUrl] = request.KSeFBaseUrl,
                [SystemConfigKeys.IsInitialized] = "true",
            };

            if (!string.IsNullOrEmpty(request.PushRelayUrl))
                configValues[SystemConfigKeys.PushRelayUrl] = request.PushRelayUrl;
            if (!string.IsNullOrEmpty(request.PushRelayApiKey))
                configValues[SystemConfigKeys.PushRelayApiKey] = request.PushRelayApiKey;
            if (!string.IsNullOrEmpty(request.FirebaseCredentialsJson))
                configValues[SystemConfigKeys.FirebaseCredentialsJson] = request.FirebaseCredentialsJson;
            if (!string.IsNullOrEmpty(request.GoogleClientId))
                configValues[SystemConfigKeys.GoogleClientId] = request.GoogleClientId;
            if (!string.IsNullOrEmpty(request.GoogleClientSecret))
                configValues[SystemConfigKeys.GoogleClientSecret] = request.GoogleClientSecret;

            await _systemConfig.SetValuesAsync(configValues, ct);
            await _systemConfig.RefreshCacheAsync(ct);
            _logger.LogInformation("System setup completed successfully");

            return new SetupApplyResponse(true, encryptionKey, apiClientSecret, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System setup failed");
            return new SetupApplyResponse(false, null, null, ex.Message);
        }
    }

    private async Task<string> FetchApiClientSecretAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var clientsUrl = $"{kcBase}/admin/realms/openksef/clients?clientId=openksef-api";
        using var req = new HttpRequestMessage(HttpMethod.Get, clientsUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");

        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var clients = await resp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("openksef-api client not found in Keycloak");

        if (clients.Length == 0)
            throw new InvalidOperationException("openksef-api client not found in Keycloak");

        var clientUuid = clients[0].GetProperty("id").GetString()!;

        var secretUrl = $"{kcBase}/admin/realms/openksef/clients/{clientUuid}/client-secret";
        using var secretReq = new HttpRequestMessage(HttpMethod.Get, secretUrl);
        secretReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");

        var secretResp = await client.SendAsync(secretReq, ct);
        secretResp.EnsureSuccessStatusCode();

        var secretJson = await secretResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var secret = secretJson.GetProperty("value").GetString();

        if (string.IsNullOrEmpty(secret))
        {
            using var genReq = new HttpRequestMessage(HttpMethod.Post, secretUrl);
            genReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            var genResp = await client.SendAsync(genReq, ct);
            genResp.EnsureSuccessStatusCode();
            var genJson = await genResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            secret = genJson.GetProperty("value").GetString()
                     ?? throw new InvalidOperationException("Failed to generate openksef-api client secret");
        }

        return secret;
    }

    private async Task EnableTokenExchangeAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var apiClientUuid = await GetClientUuidAsync(client, kcBase, adminToken, "openksef-api", ct);

        // Enable permissions on the client
        var permUrl = $"{kcBase}/admin/realms/openksef/clients/{apiClientUuid}/management/permissions";
        await SendKeycloakJsonAsync(client, HttpMethod.Put, permUrl, adminToken,
            new { enabled = true }, ct, throwOnError: false);

        // Get service account user
        var saUrl = $"{kcBase}/admin/realms/openksef/clients/{apiClientUuid}/service-account-user";
        using var saReq = new HttpRequestMessage(HttpMethod.Get, saUrl);
        saReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var saResp = await client.SendAsync(saReq, ct);
        saResp.EnsureSuccessStatusCode();
        var saJson = await saResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var saUserId = saJson.GetProperty("id").GetString()!;

        // Get realm-management client UUID
        var rmUuid = await GetClientUuidAsync(client, kcBase, adminToken, "realm-management", ct);
        if (rmUuid == null) return;

        // Get available roles and assign token-exchange
        var availUrl = $"{kcBase}/admin/realms/openksef/users/{saUserId}/role-mappings/clients/{rmUuid}/available";
        using var availReq = new HttpRequestMessage(HttpMethod.Get, availUrl);
        availReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var availResp = await client.SendAsync(availReq, ct);
        availResp.EnsureSuccessStatusCode();

        var roles = await availResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        var tokenExchangeRole = roles.FirstOrDefault(r =>
            r.GetProperty("name").GetString() == "token-exchange");

        if (tokenExchangeRole.ValueKind != JsonValueKind.Undefined)
        {
            var rolePayload = new[] { new { id = tokenExchangeRole.GetProperty("id").GetString(), name = "token-exchange" } };
            var assignUrl = $"{kcBase}/admin/realms/openksef/users/{saUserId}/role-mappings/clients/{rmUuid}";
            await SendKeycloakJsonAsync(client, HttpMethod.Post, assignUrl, adminToken, rolePayload, ct);
        }
    }

    private async Task UpdateClientRedirectUrisAsync(
        HttpClient client, string kcBase, string adminToken, string externalBaseUrl, CancellationToken ct)
    {
        var url = externalBaseUrl.TrimEnd('/');
        var redirectUris = new[] { $"{url}/*" };
        var webOrigins = new[] { url, "*" };

        foreach (var clientId in new[] { "openksef-api", "openksef-mobile", "openksef-portal-web" })
        {
            var uuid = await GetClientUuidAsync(client, kcBase, adminToken, clientId, ct);
            if (uuid == null) continue;

            var clientUrl = $"{kcBase}/admin/realms/openksef/clients/{uuid}";

            using var getReq = new HttpRequestMessage(HttpMethod.Get, clientUrl);
            getReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            var getResp = await client.SendAsync(getReq, ct);
            getResp.EnsureSuccessStatusCode();
            var clientJson = await getResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            var existingRedirects = clientJson.GetProperty("redirectUris")
                .EnumerateArray().Select(u => u.GetString()!).ToList();

            var merged = existingRedirects.Concat(redirectUris).Distinct().ToArray();

            await SendKeycloakJsonAsync(client, HttpMethod.Put, clientUrl, adminToken,
                new { redirectUris = merged, webOrigins }, ct);
        }
    }

    private async Task ConfigureRealmAsync(
        HttpClient client, string kcBase, string adminToken, SetupApplyRequest request, CancellationToken ct)
    {
        var realmUrl = $"{kcBase}/admin/realms/openksef";
        var payload = new Dictionary<string, object>
        {
            ["registrationAllowed"] = request.RegistrationAllowed,
            ["verifyEmail"] = request.VerifyEmail,
            ["loginWithEmailAllowed"] = request.LoginWithEmailAllowed,
            ["resetPasswordAllowed"] = request.ResetPasswordAllowed,
        };

        if (!string.IsNullOrEmpty(request.PasswordPolicy))
            payload["passwordPolicy"] = request.PasswordPolicy;

        if (request.Smtp is not null)
        {
            payload["smtpServer"] = new Dictionary<string, string>
            {
                ["host"] = request.Smtp.Host,
                ["port"] = request.Smtp.Port,
                ["from"] = request.Smtp.From,
                ["fromDisplayName"] = request.Smtp.FromDisplayName ?? "OpenKSeF",
                ["replyTo"] = request.Smtp.ReplyTo ?? "",
                ["starttls"] = request.Smtp.Starttls.ToString().ToLowerInvariant(),
                ["ssl"] = request.Smtp.Ssl.ToString().ToLowerInvariant(),
                ["auth"] = request.Smtp.Auth.ToString().ToLowerInvariant(),
                ["user"] = request.Smtp.User ?? "",
                ["password"] = request.Smtp.Password ?? "",
            };
        }

        await SendKeycloakJsonAsync(client, HttpMethod.Put, realmUrl, adminToken, payload, ct);
    }

    private async Task CreateAdminUserAsync(
        HttpClient client, string kcBase, string adminToken, SetupApplyRequest request, CancellationToken ct)
    {
        var usersUrl = $"{kcBase}/admin/realms/openksef/users";

        // Check if user already exists
        using var checkReq = new HttpRequestMessage(HttpMethod.Get, $"{usersUrl}?email={Uri.EscapeDataString(request.AdminEmail)}&exact=true");
        checkReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var checkResp = await client.SendAsync(checkReq, ct);
        checkResp.EnsureSuccessStatusCode();
        var existing = await checkResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];

        if (existing.Length > 0)
        {
            _logger.LogInformation("Admin user {Email} already exists, skipping creation", request.AdminEmail);
            return;
        }

        var userPayload = new
        {
            username = request.AdminEmail,
            email = request.AdminEmail,
            enabled = true,
            emailVerified = true,
            firstName = request.AdminFirstName ?? "Admin",
            lastName = string.IsNullOrWhiteSpace(request.AdminLastName) ? "Administrator" : request.AdminLastName,
        };

        var createResp = await SendKeycloakJsonAsync(client, HttpMethod.Post, usersUrl, adminToken, userPayload, ct);
        createResp.EnsureSuccessStatusCode();

        // Fetch the created user to get their ID
        using var fetchReq = new HttpRequestMessage(HttpMethod.Get, $"{usersUrl}?email={Uri.EscapeDataString(request.AdminEmail)}&exact=true");
        fetchReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var fetchResp = await client.SendAsync(fetchReq, ct);
        fetchResp.EnsureSuccessStatusCode();

        var users = await fetchResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        if (users.Length == 0)
            throw new InvalidOperationException("Failed to find created admin user");

        var userId = users[0].GetProperty("id").GetString()!;

        // Set password
        var passUrl = $"{usersUrl}/{userId}/reset-password";
        await SendKeycloakJsonAsync(client, HttpMethod.Put, passUrl, adminToken,
            new { type = "password", value = request.AdminPassword, temporary = false }, ct);
    }

    private async Task ConfigureGoogleIdpAsync(
        HttpClient client, string kcBase, string adminToken, SetupApplyRequest request, CancellationToken ct)
    {
        var idpUrl = $"{kcBase}/admin/realms/openksef/identity-provider/instances/google";

        using var getReq = new HttpRequestMessage(HttpMethod.Get, idpUrl);
        getReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var getResp = await client.SendAsync(getReq, ct);

        if (getResp.IsSuccessStatusCode)
        {
            // Update existing
            var idpJson = await getResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var payload = new Dictionary<string, object>
            {
                ["alias"] = "google",
                ["displayName"] = "Google",
                ["providerId"] = "google",
                ["enabled"] = true,
                ["trustEmail"] = true,
                ["config"] = new Dictionary<string, string>
                {
                    ["clientId"] = request.GoogleClientId!,
                    ["clientSecret"] = request.GoogleClientSecret!,
                    ["defaultScope"] = "openid email profile",
                    ["syncMode"] = "IMPORT",
                },
            };
            await SendKeycloakJsonAsync(client, HttpMethod.Put, idpUrl, adminToken, payload, ct);
        }
        else
        {
            // Create new
            var payload = new
            {
                alias = "google",
                displayName = "Google",
                providerId = "google",
                enabled = true,
                trustEmail = true,
                config = new Dictionary<string, string>
                {
                    ["clientId"] = request.GoogleClientId!,
                    ["clientSecret"] = request.GoogleClientSecret!,
                    ["defaultScope"] = "openid email profile",
                    ["syncMode"] = "IMPORT",
                },
            };
            var createUrl = $"{kcBase}/admin/realms/openksef/identity-provider/instances";
            await SendKeycloakJsonAsync(client, HttpMethod.Post, createUrl, adminToken, payload, ct);
        }
    }

    private async Task<string?> GetClientUuidAsync(
        HttpClient client, string kcBase, string adminToken, string clientId, CancellationToken ct)
    {
        var url = $"{kcBase}/admin/realms/openksef/clients?clientId={clientId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var clients = await resp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        return clients.Length > 0 ? clients[0].GetProperty("id").GetString() : null;
    }

    private static async Task<HttpResponseMessage> SendKeycloakJsonAsync(
        HttpClient client, HttpMethod method, string url, string adminToken,
        object payload, CancellationToken ct, bool throwOnError = true)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        req.Content = JsonContent.Create(payload);

        var resp = await client.SendAsync(req, ct);
        if (throwOnError)
            resp.EnsureSuccessStatusCode();
        return resp;
    }
}
