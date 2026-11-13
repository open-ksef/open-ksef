using System.Net;
using System.Text;
using System.Text.Json;
using KSeF.Client.DI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

public interface ISystemSetupService
{
    Task<(string? Token, bool Unreachable)> AuthenticateAdminAsync(string username, string password);
    Task<SetupApplyResponse> ApplySetupAsync(SetupApplyRequest request, string adminToken, string kcAdminUsername, CancellationToken ct = default);
}

public sealed class SystemSetupService : ISystemSetupService
{
    public const string EncryptionKeyMissingCode = "encryption_key_missing";
    public const string EncryptionKeyMissingMessage =
        "Brak prawidłowego klucza szyfrowania. Wygeneruj klucz poleceniem openssl rand -base64 32, ustaw go w zmiennej ENCRYPTION_KEY usług API i Worker, zachowaj kopię poza bazą danych i uruchom te usługi ponownie. Następnie zaloguj się tutaj ponownie.";
    public const string KeycloakPasswordPolicyMessage =
        "Hasło nie spełnia wymagań Keycloak (zasady haseł realmu). Użyj dłuższego hasła z wielkimi i małymi literami, cyfrą i znakiem specjalnym.";
    public const string KeycloakRejectedMessage = "Keycloak odrzucił konfigurację. Szczegóły są w logu API.";
    public const string KeycloakUnreachableMessage = "Nie można połączyć się z Keycloak. Sprawdź, czy działa, i spróbuj ponownie.";
    public const string KeycloakInvalidCredentialsMessage = "Nieprawidłowy login lub hasło administratora Keycloak.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ISystemConfigService _systemConfig;
    private readonly KSeFClientOptions _ksefOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemSetupService> _logger;

    public SystemSetupService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ISystemConfigService systemConfig,
        KSeFClientOptions ksefOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<SystemSetupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _systemConfig = systemConfig;
        _ksefOptions = ksefOptions;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Keycloak keeps the stored SMTP password when it receives this mask; omitting the key or sending "" replaces it.</summary>
    internal static string KeycloakSmtpPassword(string? password) =>
        string.IsNullOrEmpty(password) ? "**********" : password;

    internal static readonly IReadOnlyDictionary<string, object> BruteForceSettings = new Dictionary<string, object>
    {
        ["bruteForceProtected"] = true,
        ["permanentLockout"] = false,
        ["failureFactor"] = 10,
        ["waitIncrementSeconds"] = 60,
        ["maxFailureWaitSeconds"] = 900,
        ["maxDeltaTimeSeconds"] = 43200,
        ["quickLoginCheckMilliSeconds"] = 1000,
        ["minimumQuickLoginWaitSeconds"] = 60,
    };

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

    public async Task<(string? Token, bool Unreachable)> AuthenticateAdminAsync(string username, string password)
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
                return (null, response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest));
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (json.GetProperty("access_token").GetString(), false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate Keycloak admin");
            return (null, true);
        }
    }

    public static bool HasUsableEncryptionKey(IConfiguration configuration)
    {
        try
        {
            return EncryptionServiceExtensions.ReadConfiguredKey(configuration) is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<SetupApplyResponse> ApplySetupAsync(
        SetupApplyRequest request, string adminToken, string kcAdminUsername, CancellationToken ct = default)
    {
        try
        {
            if (!HasUsableEncryptionKey(_configuration))
                return new SetupApplyResponse(false, EncryptionKeyMissingMessage, EncryptionKeyMissingCode);

            var ksefEnv = DependencyInjection.NormalizeKSeFEnvironmentKey(request.KSeFBaseUrl);

            if (!string.IsNullOrWhiteSpace(request.FirstTenantNip) && !NipValidator.HasValidChecksum(request.FirstTenantNip))
                return new SetupApplyResponse(false, "Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.");

            var client = _httpClientFactory.CreateClient("keycloak");
            var kcBase = KeycloakInternalBaseUrl;

            await EnsureRealmExistsAsync(client, kcBase, adminToken, ct);
            await EnsureAdminRoleAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Keycloak realm verified/created");

            await EnsureClientsExistAsync(client, kcBase, adminToken, ct);
            await EnsureApiAudienceMappersAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Keycloak clients verified/created");

            var apiClientSecret = await FetchApiClientSecretAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Fetched API client secret");

            await UpdateClientRedirectUrisAsync(client, kcBase, adminToken, request.ExternalBaseUrl, ct);
            _logger.LogInformation("Updated client redirect URIs");

            await ConfigureRealmAsync(client, kcBase, adminToken, request, ct);
            await EnsureRealmPolicyAsync(client, kcBase, adminToken, ct);
            _logger.LogInformation("Configured Keycloak realm settings");

            // Keycloak 26+ enables VERIFY_PROFILE by default, and it blocks login.
            await DisableVerifyProfileActionAsync(client, kcBase, adminToken, ct);

            await CreateFirstUserAsync(client, kcBase, adminToken, request, ct);
            _logger.LogInformation("Created first user");

            if (!string.IsNullOrEmpty(request.GoogleClientId) && !string.IsNullOrEmpty(request.GoogleClientSecret))
            {
                await ConfigureGoogleIdpAsync(client, kcBase, adminToken, request, ct);
                _logger.LogInformation("Configured Google IdP");
            }

            // Before is_initialized is stored, so a failure here does not lock out the wizard.
            if (!string.IsNullOrEmpty(request.NewKeycloakAdminPassword))
            {
                await ChangeKeycloakAdminPasswordAsync(client, kcBase, adminToken, kcAdminUsername, request.NewKeycloakAdminPassword, ct);
                _logger.LogInformation("Keycloak admin password changed");
            }

            var configValues = new Dictionary<string, string>
            {
                [SystemConfigKeys.ApiClientSecret] = apiClientSecret,
                [SystemConfigKeys.ExternalBaseUrl] = request.ExternalBaseUrl.TrimEnd('/'),
                [SystemConfigKeys.KSeFEnvironment] = ksefEnv,
                [SystemConfigKeys.RegistrationAllowed] = request.RegistrationAllowed ? "true" : "false",
                [SystemConfigKeys.IsInitialized] = "true",
            };

            if (!string.IsNullOrEmpty(request.PushRelayUrl))
            {
                configValues[SystemConfigKeys.PushRelayUrl] = request.PushRelayUrl;

                // Auto-register with the relay if no manual API key was provided
                if (string.IsNullOrEmpty(request.PushRelayApiKey))
                {
                    try
                    {
                        var (relayInstanceId, relayApiKey) = await RegisterWithRelayAsync(
                            request.PushRelayUrl, request.ExternalBaseUrl, ct);
                        configValues[SystemConfigKeys.PushRelayApiKey] = relayApiKey;
                        configValues[SystemConfigKeys.PushRelayInstanceId] = relayInstanceId;
                        _logger.LogInformation("Registered with push relay, instanceId: {InstanceId}", relayInstanceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to register with push relay -- push notifications will use SignalR only until registration succeeds");
                    }
                }
                else
                {
                    configValues[SystemConfigKeys.PushRelayApiKey] = request.PushRelayApiKey;
                }
            }
            if (!string.IsNullOrEmpty(request.FirebaseCredentialsJson))
                configValues[SystemConfigKeys.FirebaseCredentialsJson] = request.FirebaseCredentialsJson;
            if (!string.IsNullOrEmpty(request.GoogleClientId))
                configValues[SystemConfigKeys.GoogleClientId] = request.GoogleClientId;
            if (!string.IsNullOrEmpty(request.GoogleClientSecret))
                configValues[SystemConfigKeys.GoogleClientSecret] = request.GoogleClientSecret;

            await _systemConfig.SetValuesAsync(configValues, ct);
            await _systemConfig.RefreshCacheAsync(ct);

            _ksefOptions.BaseUrl = DependencyInjection.ResolveKSeFEnvironment(ksefEnv);
            _logger.LogInformation("KSeF environment set to {Env} ({Url})",
                ksefEnv, _ksefOptions.BaseUrl);

            _logger.LogInformation("System setup completed successfully");

            return new SetupApplyResponse(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System setup failed");
            return new SetupApplyResponse(false, ex switch
            {
                HttpRequestException when ex.Message.Contains("invalidPassword", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("password policy", StringComparison.OrdinalIgnoreCase) => KeycloakPasswordPolicyMessage,
                HttpRequestException { StatusCode: null } => KeycloakUnreachableMessage,
                HttpRequestException => KeycloakRejectedMessage,
                _ => ex.Message,
            });
        }
    }

    private async Task EnsureRealmExistsAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var realmUrl = $"{kcBase}/admin/realms/openksef";
        using var checkReq = new HttpRequestMessage(HttpMethod.Get, realmUrl);
        checkReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var checkResp = await client.SendAsync(checkReq, ct);

        if (checkResp.IsSuccessStatusCode)
        {
            _logger.LogInformation("Realm 'openksef' already exists");
            return;
        }

        var realmPayload = new Dictionary<string, object>
        {
            ["realm"] = "openksef",
            ["enabled"] = true,
            ["displayName"] = "openksef",
            ["sslRequired"] = "external",
            ["accessTokenLifespan"] = 300,
            ["ssoSessionIdleTimeout"] = 1800,
            ["ssoSessionMaxLifespan"] = 36000,
            ["registrationAllowed"] = true,
            ["loginWithEmailAllowed"] = true,
            ["duplicateEmailsAllowed"] = false,
            ["resetPasswordAllowed"] = true,
            ["editUsernameAllowed"] = false,
        };
        foreach (var (key, value) in BruteForceSettings)
            realmPayload[key] = value;

        await SendKeycloakJsonAsync(client, HttpMethod.Post,
            $"{kcBase}/admin/realms", adminToken, realmPayload, ct);

        _logger.LogInformation("Created realm 'openksef'");
    }

    private static async Task EnsureAdminRoleAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var resp = await SendKeycloakJsonAsync(client, HttpMethod.Post, $"{kcBase}/admin/realms/openksef/roles", adminToken,
            new { name = SystemSettingsController.AdminRole }, ct, throwOnError: false);
        if (resp.StatusCode != HttpStatusCode.Conflict)
            resp.EnsureSuccessStatusCode();
    }

    private async Task EnsureClientsExistAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var clientsUrl = $"{kcBase}/admin/realms/openksef/clients";

        var clientDefinitions = new[]
        {
            new Dictionary<string, object>
            {
                ["clientId"] = "openksef-api",
                ["name"] = "openksef-api",
                ["enabled"] = true,
                ["protocol"] = "openid-connect",
                ["publicClient"] = false,
                ["standardFlowEnabled"] = true,
                ["directAccessGrantsEnabled"] = false,
                ["serviceAccountsEnabled"] = true,
                ["redirectUris"] = new[] { "/*" },
                ["webOrigins"] = new[] { "+" },
            },
            new Dictionary<string, object>
            {
                ["clientId"] = "openksef-mobile",
                ["name"] = "openksef-mobile",
                ["enabled"] = true,
                ["protocol"] = "openid-connect",
                ["publicClient"] = true,
                ["standardFlowEnabled"] = true,
                ["directAccessGrantsEnabled"] = true,
                ["redirectUris"] = new[] { "/*", "openksef://auth/*" },
                ["webOrigins"] = new[] { "+" },
            },
            new Dictionary<string, object>
            {
                ["clientId"] = "openksef-portal-web",
                ["name"] = "openksef-portal-web",
                ["enabled"] = true,
                ["protocol"] = "openid-connect",
                ["publicClient"] = true,
                ["standardFlowEnabled"] = true,
                ["implicitFlowEnabled"] = false,
                ["directAccessGrantsEnabled"] = true,
                ["serviceAccountsEnabled"] = false,
                ["redirectUris"] = new[] { "/*" },
                ["webOrigins"] = new[] { "+" },
                ["attributes"] = new Dictionary<string, string>
                {
                    ["pkce.code.challenge.method"] = "S256",
                },
            },
        };

        foreach (var clientDef in clientDefinitions)
        {
            var clientId = (string)clientDef["clientId"];
            var existing = await GetClientUuidAsync(client, kcBase, adminToken, clientId, ct);
            if (existing != null)
            {
                _logger.LogInformation("Client '{ClientId}' already exists", clientId);
                continue;
            }

            await SendKeycloakJsonAsync(client, HttpMethod.Post, clientsUrl, adminToken, clientDef, ct);
            _logger.LogInformation("Created client '{ClientId}'", clientId);
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

    private async Task UpdateClientRedirectUrisAsync(
        HttpClient client, string kcBase, string adminToken, string externalBaseUrl, CancellationToken ct)
    {
        var url = externalBaseUrl.TrimEnd('/');
        var redirectUris = new[] { $"{url}/*" };
        var webOrigins = new[] { url };

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

            // Keycloak 26.0 replaces the service account user, losing its roles, when a client PUT omits this field.
            var serviceAccountsEnabled = clientJson.GetProperty("serviceAccountsEnabled").GetBoolean();

            await SendKeycloakJsonAsync(client, HttpMethod.Put, clientUrl, adminToken,
                new { redirectUris = merged, webOrigins, serviceAccountsEnabled }, ct);
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
            ["accessTokenLifespan"] = 300,
            ["ssoSessionIdleTimeout"] = 1800,
            ["ssoSessionMaxLifespan"] = 36000,
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
                ["password"] = KeycloakSmtpPassword(request.Smtp.Password),
            };
        }

        await SendKeycloakJsonAsync(client, HttpMethod.Put, realmUrl, adminToken, payload, ct);
    }

    private async Task DisableVerifyProfileActionAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var url = $"{kcBase}/admin/realms/openksef/authentication/required-actions/VERIFY_PROFILE";
        var payload = new Dictionary<string, object>
        {
            ["alias"] = "VERIFY_PROFILE",
            ["name"] = "Verify Profile",
            ["providerId"] = "VERIFY_PROFILE",
            ["enabled"] = false,
            ["defaultAction"] = false,
            ["priority"] = 90,
        };

        try
        {
            var resp = await SendKeycloakJsonAsync(client, HttpMethod.Put, url, adminToken, payload, ct, throwOnError: false);
            if (resp.IsSuccessStatusCode)
                _logger.LogInformation("Disabled VERIFY_PROFILE required action");
            else
                _logger.LogWarning("Failed to disable VERIFY_PROFILE (HTTP {StatusCode}) — login may require profile verification",
                    (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to disable VERIFY_PROFILE required action — login may require profile verification");
        }
    }

    private async Task CreateFirstUserAsync(
        HttpClient client, string kcBase, string adminToken, SetupApplyRequest request, CancellationToken ct)
    {
        var usersUrl = $"{kcBase}/admin/realms/openksef/users";

        string userId;

        // Check if user already exists
        using var checkReq = new HttpRequestMessage(HttpMethod.Get, $"{usersUrl}?email={Uri.EscapeDataString(request.AdminEmail)}&exact=true");
        checkReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var checkResp = await client.SendAsync(checkReq, ct);
        checkResp.EnsureSuccessStatusCode();
        var existing = await checkResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];

        if (existing.Length > 0)
        {
            userId = existing[0].GetProperty("id").GetString()!;
            _logger.LogInformation("User {Email} already exists, setting its password", request.AdminEmail);
        }
        else
        {
            var userPayload = new
            {
                username = request.AdminEmail,
                email = request.AdminEmail,
                enabled = true,
                emailVerified = true,
                firstName = request.AdminFirstName ?? "",
                lastName = request.AdminLastName ?? "",
            };

            var createResp = await SendKeycloakJsonAsync(client, HttpMethod.Post, usersUrl, adminToken, userPayload, ct);
            createResp.EnsureSuccessStatusCode();

            using var fetchReq = new HttpRequestMessage(HttpMethod.Get, $"{usersUrl}?email={Uri.EscapeDataString(request.AdminEmail)}&exact=true");
            fetchReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            var fetchResp = await client.SendAsync(fetchReq, ct);
            fetchResp.EnsureSuccessStatusCode();

            var users = await fetchResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
            if (users.Length == 0)
                throw new InvalidOperationException("Failed to find created user");

            userId = users[0].GetProperty("id").GetString()!;
        }

        await SendKeycloakJsonAsync(client, HttpMethod.Put, $"{usersUrl}/{userId}/reset-password", adminToken,
            new { type = "password", value = request.AdminPassword, temporary = false }, ct);

        using var roleReq = new HttpRequestMessage(HttpMethod.Get, $"{kcBase}/admin/realms/openksef/roles/{SystemSettingsController.AdminRole}");
        roleReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var roleResp = await client.SendAsync(roleReq, ct);
        roleResp.EnsureSuccessStatusCode();
        var adminRole = await roleResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        await SendKeycloakJsonAsync(client, HttpMethod.Post, $"{usersUrl}/{userId}/role-mappings/realm", adminToken, new[] { adminRole }, ct);

        // Create first tenant in DB if NIP was provided
        if (!string.IsNullOrWhiteSpace(request.FirstTenantNip))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var nip = NipValidator.Normalize(request.FirstTenantNip);
            var tenantExists = await db.Tenants.AnyAsync(t => t.UserId == userId && t.Nip == nip, ct);
            if (!tenantExists)
            {
                var now = DateTime.UtcNow;
                db.Tenants.Add(new Tenant
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Nip = nip,
                    DisplayName = request.FirstTenantDisplayName,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Created first tenant with NIP {Nip} for user {UserId}", nip, userId);
            }
        }
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

    private async Task ChangeKeycloakAdminPasswordAsync(
        HttpClient client, string kcBase, string adminToken,
        string kcAdminUsername, string newPassword, CancellationToken ct)
    {
        var usersUrl = $"{kcBase}/admin/realms/master/users?username={Uri.EscapeDataString(kcAdminUsername)}&exact=true";
        using var findReq = new HttpRequestMessage(HttpMethod.Get, usersUrl);
        findReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");

        var findResp = await client.SendAsync(findReq, ct);
        findResp.EnsureSuccessStatusCode();

        var users = await findResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        if (users.Length == 0)
            throw new InvalidOperationException($"Keycloak admin user '{kcAdminUsername}' not found in master realm");

        var userId = users[0].GetProperty("id").GetString()!;

        var passUrl = $"{kcBase}/admin/realms/master/users/{userId}/reset-password";
        await SendKeycloakJsonAsync(client, HttpMethod.Put, passUrl, adminToken,
            new { type = "password", value = newPassword, temporary = false }, ct);
    }

    internal static async Task EnsureApiAudienceMappersAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        foreach (var clientId in new[] { "openksef-mobile", "openksef-portal-web" })
        {
            var uuid = await GetClientUuidAsync(client, kcBase, adminToken, clientId, ct);
            if (uuid == null) continue;

            var mappersUrl = $"{kcBase}/admin/realms/openksef/clients/{uuid}/protocol-mappers/models";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, mappersUrl);
            getReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            using var getResp = await client.SendAsync(getReq, ct);
            getResp.EnsureSuccessStatusCode();
            var mappers = await getResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
            if (mappers.Any(m => m.TryGetProperty("name", out var name) && name.GetString() == "openksef-api-audience"
                    || m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"
                    && m.TryGetProperty("config", out var config)
                    && (config.TryGetProperty("included.client.audience", out var audience) && audience.GetString() == "openksef-api"
                        || config.TryGetProperty("included.custom.audience", out var custom) && custom.GetString() == "openksef-api")
                    && !(config.TryGetProperty("access.token.claim", out var inAccessToken) && inAccessToken.GetString() == "false")))
                continue;

            await SendKeycloakJsonAsync(client, HttpMethod.Post, mappersUrl, adminToken, new Dictionary<string, object>
            {
                ["name"] = "openksef-api-audience",
                ["protocol"] = "openid-connect",
                ["protocolMapper"] = "oidc-audience-mapper",
                ["config"] = new Dictionary<string, string>
                {
                    ["included.client.audience"] = "openksef-api",
                    ["access.token.claim"] = "true",
                    ["id.token.claim"] = "false",
                },
            }, ct);
        }
    }

    public static readonly IReadOnlyDictionary<string, string> PolishLoginTexts = new Dictionary<string, string>
    {
        ["oauthGrantTitle"] = "Zezwolić na logowanie: {0}?",
        ["oauthGrantRequest"] = "Zatwierdź tylko, jeśli ten kod widzisz teraz na swoim telefonie. Jeśli ktoś podał Ci kod lub link, wybierz Nie: ta osoba dostałaby dostęp do Twoich firm i faktur, także do wystawiania faktur w KSeF, bez Twojego hasła, nawet na rok. Aplikacja otrzyma:",
        ["oauth2DeviceVerificationTitle"] = "Logowanie telefonu",
        ["verifyOAuth2DeviceUserCode"] = "Wpisz kod widoczny w aplikacji na Twoim telefonie i wybierz Zatwierdź",
        ["oauth2DeviceInvalidUserCodeMessage"] = "Nieprawidłowy kod. Spróbuj ponownie.",
        ["oauth2DeviceExpiredUserCodeMessage"] = "Kod wygasł. Wróć do telefonu i poproś o nowy kod.",
        ["oauth2DeviceVerificationCompleteHeader"] = "Telefon zalogowany",
        ["oauth2DeviceVerificationCompleteMessage"] = "Możesz zamknąć tę kartę i wrócić do telefonu.",
        ["oauth2DeviceVerificationFailedHeader"] = "Nie zalogowano telefonu",
        ["oauth2DeviceVerificationFailedMessage"] = "Możesz zamknąć tę kartę, wrócić do telefonu i spróbować ponownie.",
        ["oauth2DeviceConsentDeniedMessage"] = "Odmówiono logowania telefonu.",
        ["oauth2DeviceAuthorizationGrantDisabledMessage"] = "Ten serwer nie obsługuje parowania kodem.",
    };

    internal static async Task EnsureRealmPolicyAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var realmUrl = $"{kcBase}/admin/realms/openksef";
        await SendKeycloakJsonAsync(client, HttpMethod.Put, realmUrl, adminToken, new Dictionary<string, object>
        {
            ["displayName"] = "OpenKSeF",
            ["loginTheme"] = "openksef",
            ["internationalizationEnabled"] = true,
            ["supportedLocales"] = new[] { "pl" },
            ["defaultLocale"] = "pl",
            ["accessTokenLifespan"] = 300,
            ["offlineSessionIdleTimeout"] = 2592000,
            ["offlineSessionMaxLifespanEnabled"] = true,
            ["offlineSessionMaxLifespan"] = 31536000,
            ["eventsEnabled"] = true,
            ["eventsExpiration"] = 2592000,
            ["enabledEventTypes"] = new[] { "LOGIN", "LOGIN_ERROR", "OAUTH2_DEVICE_VERIFY_USER_CODE", "OAUTH2_DEVICE_CODE_TO_TOKEN", "REVOKE_GRANT" },
        }, ct);

        foreach (var (key, text) in PolishLoginTexts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{realmUrl}/localization/pl/{key}");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            req.Content = new StringContent(text, Encoding.UTF8, "text/plain");
            using var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }

        var uuid = await GetClientUuidAsync(client, kcBase, adminToken, "openksef-mobile", ct);
        if (uuid == null) return;

        var clientUrl = $"{realmUrl}/clients/{uuid}";
        await SendKeycloakJsonAsync(client, HttpMethod.Put, clientUrl, adminToken, new Dictionary<string, object>
        {
            ["name"] = "Aplikacja mobilna OpenKSeF",
            ["attributes"] = new Dictionary<string, string>
            {
                ["oauth2.device.authorization.grant.enabled"] = "true",
                ["oauth2.device.code.lifespan"] = "300",
                ["pkce.code.challenge.method"] = "S256",
            },
        }, ct);

        using var scopesReq = new HttpRequestMessage(HttpMethod.Get, $"{clientUrl}/optional-client-scopes");
        scopesReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        using var scopesResp = await client.SendAsync(scopesReq, ct);
        scopesResp.EnsureSuccessStatusCode();
        var optional = await scopesResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        if (optional.Any(s => s.GetProperty("name").GetString() == "offline_access"))
            return;

        using var allReq = new HttpRequestMessage(HttpMethod.Get, $"{realmUrl}/client-scopes");
        allReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        using var allResp = await client.SendAsync(allReq, ct);
        allResp.EnsureSuccessStatusCode();
        var scopes = await allResp.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: ct) ?? [];
        var offlineAccess = scopes.Single(s => s.GetProperty("name").GetString() == "offline_access").GetProperty("id").GetString();

        using var addReq = new HttpRequestMessage(HttpMethod.Put, $"{clientUrl}/optional-client-scopes/{offlineAccess}");
        addReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        using var addResp = await client.SendAsync(addReq, ct);
        addResp.EnsureSuccessStatusCode();
    }

    private static async Task<string?> GetClientUuidAsync(
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

    private async Task<(string InstanceId, string ApiKey)> RegisterWithRelayAsync(
        string relayUrl, string externalBaseUrl, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("push-relay");
        var registerUrl = $"{relayUrl.TrimEnd('/')}/api/register";

        var payload = new { instanceUrl = externalBaseUrl.TrimEnd('/') };
        var response = await client.PostAsJsonAsync(registerUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var instanceId = result.GetProperty("instanceId").GetString()
            ?? throw new InvalidOperationException("Relay registration returned no instanceId");
        var apiKey = result.GetProperty("apiKey").GetString()
            ?? throw new InvalidOperationException("Relay registration returned no apiKey");

        return (instanceId, apiKey);
    }

    private static async Task<HttpResponseMessage> SendKeycloakJsonAsync(
        HttpClient client, HttpMethod method, string url, string adminToken,
        object payload, CancellationToken ct, bool throwOnError = true)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        req.Content = JsonContent.Create(payload);

        var resp = await client.SendAsync(req, ct);
        if (throwOnError && !resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Keycloak {method} {req.RequestUri!.AbsolutePath} returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}",
                null, resp.StatusCode);
        return resp;
    }
}
