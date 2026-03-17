using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

public interface ISystemSettingsService
{
    Task<SettingsResponse> GetSettingsAsync(string kcAdminToken, CancellationToken ct = default);
    Task<SettingsUpdateResponse> UpdateSettingsAsync(SettingsUpdateRequest request, string kcAdminToken, CancellationToken ct = default);
    Task<(bool HasInvoices, bool HasCredentials)> GetKSeFEnvironmentLockStatusAsync(CancellationToken ct = default);
}

public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ISystemConfigService _systemConfig;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemSettingsService> _logger;

    public SystemSettingsService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ISystemConfigService systemConfig,
        IServiceScopeFactory scopeFactory,
        ILogger<SystemSettingsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _systemConfig = systemConfig;
        _scopeFactory = scopeFactory;
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

    public async Task<SettingsResponse> GetSettingsAsync(string kcAdminToken, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("keycloak");
        var kcBase = KeycloakInternalBaseUrl;

        var realmConfig = await GetRealmConfigAsync(client, kcBase, kcAdminToken, ct);
        var googleConfigured = await IsGoogleIdpConfiguredAsync(client, kcBase, kcAdminToken, ct);
        var (hasInvoices, hasCredentials) = await GetKSeFEnvironmentLockStatusAsync(ct);

        var ksefEnv = _systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
            ?? DependencyInjection.NormalizeKSeFEnvironmentKey(
                _systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl));

        string? lockReason = null;
        var locked = false;
        if (hasInvoices)
        {
            locked = true;
            lockReason = "Nie można zmienić środowiska KSeF -- w systemie istnieją faktury.";
        }
        else if (hasCredentials)
        {
            lockReason = "Zmiana środowiska KSeF wymaga usunięcia wszystkich poświadczeń KSeF (tokenów i certyfikatów).";
        }

        return new SettingsResponse
        {
            ExternalBaseUrl = _systemConfig.GetValue(SystemConfigKeys.ExternalBaseUrl),
            KSeFEnvironment = ksefEnv,
            KSeFEnvironmentLocked = locked,
            KSeFEnvironmentLockReason = lockReason,

            RegistrationAllowed = realmConfig.RegistrationAllowed,
            VerifyEmail = realmConfig.VerifyEmail,
            LoginWithEmailAllowed = realmConfig.LoginWithEmailAllowed,
            ResetPasswordAllowed = realmConfig.ResetPasswordAllowed,
            PasswordPolicy = realmConfig.PasswordPolicy,
            Smtp = realmConfig.Smtp,

            GoogleClientId = _systemConfig.GetValue(SystemConfigKeys.GoogleClientId),
            GoogleConfigured = googleConfigured,
            PushRelayUrl = _systemConfig.GetValue(SystemConfigKeys.PushRelayUrl),
            PushRelayApiKey = _systemConfig.GetValue(SystemConfigKeys.PushRelayApiKey),
            FirebaseConfigured = !string.IsNullOrEmpty(_systemConfig.GetValue(SystemConfigKeys.FirebaseCredentialsJson)),
        };
    }

    public async Task<SettingsUpdateResponse> UpdateSettingsAsync(
        SettingsUpdateRequest request, string kcAdminToken, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("keycloak");
            var kcBase = KeycloakInternalBaseUrl;

            // Validate KSeF environment change before doing any work
            var needsCredentialWipe = false;
            if (!string.IsNullOrEmpty(request.KSeFEnvironment))
            {
                var currentEnv = _systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
                    ?? DependencyInjection.NormalizeKSeFEnvironmentKey(
                        _systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl));

                if (!string.Equals(currentEnv, request.KSeFEnvironment, StringComparison.OrdinalIgnoreCase))
                {
                    var (hasInvoices, hasCredentials) = await GetKSeFEnvironmentLockStatusAsync(ct);
                    if (hasInvoices)
                        return new SettingsUpdateResponse(false, "Nie można zmienić środowiska KSeF -- w systemie istnieją faktury.");

                    if (hasCredentials)
                    {
                        if (!request.ConfirmCredentialWipe)
                            return new SettingsUpdateResponse(false, "Zmiana środowiska KSeF wymaga usunięcia wszystkich poświadczeń KSeF. Potwierdź operację.");

                        needsCredentialWipe = true;
                    }
                }
            }

            // Update Keycloak realm settings
            await UpdateRealmConfigAsync(client, kcBase, kcAdminToken, request, ct);
            _logger.LogInformation("Updated Keycloak realm settings");

            // Update Google IdP
            if (!string.IsNullOrEmpty(request.GoogleClientId) && !string.IsNullOrEmpty(request.GoogleClientSecret))
            {
                await ConfigureGoogleIdpAsync(client, kcBase, kcAdminToken, request.GoogleClientId, request.GoogleClientSecret, ct);
                _logger.LogInformation("Updated Google IdP configuration");
            }

            // Update DB config values
            var configValues = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(request.ExternalBaseUrl))
            {
                var url = request.ExternalBaseUrl.TrimEnd('/');
                configValues[SystemConfigKeys.ExternalBaseUrl] = url;
                await UpdateClientRedirectUrisAsync(client, kcBase, kcAdminToken, url, ct);
            }

            if (!string.IsNullOrEmpty(request.KSeFEnvironment))
                configValues[SystemConfigKeys.KSeFEnvironment] = request.KSeFEnvironment;

            if (request.GoogleClientId != null)
                configValues[SystemConfigKeys.GoogleClientId] = request.GoogleClientId;
            if (request.GoogleClientSecret != null)
                configValues[SystemConfigKeys.GoogleClientSecret] = request.GoogleClientSecret;
            if (request.PushRelayUrl != null)
                configValues[SystemConfigKeys.PushRelayUrl] = request.PushRelayUrl;
            if (request.PushRelayApiKey != null)
                configValues[SystemConfigKeys.PushRelayApiKey] = request.PushRelayApiKey;
            if (request.FirebaseCredentialsJson != null)
                configValues[SystemConfigKeys.FirebaseCredentialsJson] = request.FirebaseCredentialsJson;

            if (configValues.Count > 0)
            {
                await _systemConfig.SetValuesAsync(configValues, ct);
                await _systemConfig.RefreshCacheAsync(ct);
            }

            // Wipe credentials only after all other operations succeeded
            if (needsCredentialWipe)
            {
                await WipeKSeFCredentialsAsync(ct);
                _logger.LogWarning("Wiped all KSeF credentials due to environment change to {Env}", request.KSeFEnvironment);
            }

            _logger.LogInformation("Settings updated successfully");
            return new SettingsUpdateResponse(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings");
            return new SettingsUpdateResponse(false, ex.Message);
        }
    }

    public async Task<(bool HasInvoices, bool HasCredentials)> GetKSeFEnvironmentLockStatusAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hasInvoices = await db.InvoiceHeaders.AnyAsync(ct);
        var hasCredentials = await db.KSeFCredentials.AnyAsync(ct);

        return (hasInvoices, hasCredentials);
    }

    private async Task<RealmConfig> GetRealmConfigAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var realmUrl = $"{kcBase}/admin/realms/openksef";
        using var req = new HttpRequestMessage(HttpMethod.Get, realmUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        SmtpConfig? smtp = null;
        if (json.TryGetProperty("smtpServer", out var smtpJson) && smtpJson.ValueKind == JsonValueKind.Object)
        {
            var host = smtpJson.TryGetProperty("host", out var h) ? h.GetString() : null;
            if (!string.IsNullOrEmpty(host))
            {
                smtp = new SmtpConfig
                {
                    Host = host,
                    Port = smtpJson.TryGetProperty("port", out var p) ? p.GetString() ?? "587" : "587",
                    From = smtpJson.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "",
                    FromDisplayName = smtpJson.TryGetProperty("fromDisplayName", out var fd) ? fd.GetString() : null,
                    ReplyTo = smtpJson.TryGetProperty("replyTo", out var rt) ? rt.GetString() : null,
                    Starttls = smtpJson.TryGetProperty("starttls", out var st) && string.Equals(st.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                    Ssl = smtpJson.TryGetProperty("ssl", out var ss) && string.Equals(ss.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                    Auth = smtpJson.TryGetProperty("auth", out var au) && string.Equals(au.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                    User = smtpJson.TryGetProperty("user", out var u) ? u.GetString() : null,
                };
            }
        }

        return new RealmConfig
        {
            RegistrationAllowed = json.TryGetProperty("registrationAllowed", out var ra) && ra.GetBoolean(),
            VerifyEmail = json.TryGetProperty("verifyEmail", out var ve) && ve.GetBoolean(),
            LoginWithEmailAllowed = json.TryGetProperty("loginWithEmailAllowed", out var le) && le.GetBoolean(),
            ResetPasswordAllowed = json.TryGetProperty("resetPasswordAllowed", out var rp) && rp.GetBoolean(),
            PasswordPolicy = json.TryGetProperty("passwordPolicy", out var pp) ? pp.GetString() : null,
            Smtp = smtp,
        };
    }

    private async Task<bool> IsGoogleIdpConfiguredAsync(
        HttpClient client, string kcBase, string adminToken, CancellationToken ct)
    {
        var idpUrl = $"{kcBase}/admin/realms/openksef/identity-provider/instances/google";
        using var req = new HttpRequestMessage(HttpMethod.Get, idpUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var resp = await client.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private async Task UpdateRealmConfigAsync(
        HttpClient client, string kcBase, string adminToken, SettingsUpdateRequest request, CancellationToken ct)
    {
        var realmUrl = $"{kcBase}/admin/realms/openksef";
        var payload = new Dictionary<string, object>();

        if (request.RegistrationAllowed.HasValue)
            payload["registrationAllowed"] = request.RegistrationAllowed.Value;
        if (request.VerifyEmail.HasValue)
            payload["verifyEmail"] = request.VerifyEmail.Value;
        if (request.LoginWithEmailAllowed.HasValue)
            payload["loginWithEmailAllowed"] = request.LoginWithEmailAllowed.Value;
        if (request.ResetPasswordAllowed.HasValue)
            payload["resetPasswordAllowed"] = request.ResetPasswordAllowed.Value;
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
        else if (request.ClearSmtp)
        {
            payload["smtpServer"] = new Dictionary<string, string>();
        }

        if (payload.Count > 0)
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, realmUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            req.Content = JsonContent.Create(payload);
            var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }
    }

    private async Task ConfigureGoogleIdpAsync(
        HttpClient client, string kcBase, string adminToken,
        string clientId, string clientSecret, CancellationToken ct)
    {
        var idpUrl = $"{kcBase}/admin/realms/openksef/identity-provider/instances/google";

        using var getReq = new HttpRequestMessage(HttpMethod.Get, idpUrl);
        getReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
        var getResp = await client.SendAsync(getReq, ct);

        var payload = new Dictionary<string, object>
        {
            ["alias"] = "google",
            ["displayName"] = "Google",
            ["providerId"] = "google",
            ["enabled"] = true,
            ["trustEmail"] = true,
            ["config"] = new Dictionary<string, string>
            {
                ["clientId"] = clientId,
                ["clientSecret"] = clientSecret,
                ["defaultScope"] = "openid email profile",
                ["syncMode"] = "IMPORT",
            },
        };

        if (getResp.IsSuccessStatusCode)
        {
            using var putReq = new HttpRequestMessage(HttpMethod.Put, idpUrl);
            putReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            putReq.Content = JsonContent.Create(payload);
            var putResp = await client.SendAsync(putReq, ct);
            putResp.EnsureSuccessStatusCode();
        }
        else
        {
            var createUrl = $"{kcBase}/admin/realms/openksef/identity-provider/instances";
            using var postReq = new HttpRequestMessage(HttpMethod.Post, createUrl);
            postReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            postReq.Content = JsonContent.Create(payload);
            var postResp = await client.SendAsync(postReq, ct);
            postResp.EnsureSuccessStatusCode();
        }
    }

    private async Task UpdateClientRedirectUrisAsync(
        HttpClient client, string kcBase, string adminToken, string externalBaseUrl, CancellationToken ct)
    {
        var redirectUris = new[] { $"{externalBaseUrl}/*" };
        var webOrigins = new[] { externalBaseUrl, "*" };

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

            using var putReq = new HttpRequestMessage(HttpMethod.Put, clientUrl);
            putReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {adminToken}");
            putReq.Content = JsonContent.Create(new { redirectUris = merged, webOrigins });
            var putResp = await client.SendAsync(putReq, ct);
            putResp.EnsureSuccessStatusCode();
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

    private async Task WipeKSeFCredentialsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.KSeFCredentials.ExecuteDeleteAsync(ct);
    }

    private sealed class RealmConfig
    {
        public bool RegistrationAllowed { get; init; }
        public bool VerifyEmail { get; init; }
        public bool LoginWithEmailAllowed { get; init; }
        public bool ResetPasswordAllowed { get; init; }
        public string? PasswordPolicy { get; init; }
        public SmtpConfig? Smtp { get; init; }
    }
}
