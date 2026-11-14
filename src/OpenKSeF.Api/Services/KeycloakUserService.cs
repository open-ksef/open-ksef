using System.Net;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

public interface IKeycloakUserService
{
    Task<CreateUserResult> CreateUserAsync(string email, string password, string? firstName, string? lastName);
    Task<bool> CanRegisterAsync(string? clientSecret);
    Task<TwoFactorLookupResult> GetTwoFactorAsync(string email);
    Task<TwoFactorResetResult> ResetTwoFactorAsync(string userId, string actorUserId, string? actorEmail);
}

public record CreateUserResult(bool Success, string? ErrorMessage, HttpStatusCode StatusCode, string? ErrorCode = null);

public record TwoFactorLookupResult(bool KeycloakUnavailable, bool UserNotFound, TwoFactorUserResponse? User);

public enum TwoFactorResetResult { Reset, UserNotFound, ResetUnavailable, KeycloakUnavailable }

public class KeycloakUserService : IKeycloakUserService
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenEndpoint;
    private readonly string _adminRealmEndpoint;
    private readonly string _adminUsersEndpoint;
    private readonly string _clientId;
    private readonly ISystemConfigService _systemConfig;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KeycloakUserService> _logger;

    public KeycloakUserService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ISystemConfigService systemConfig,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<KeycloakUserService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("keycloak");
        _systemConfig = systemConfig;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
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
            _adminRealmEndpoint = $"{baseUrl}/admin/realms/{realmName}";
        }
        else
        {
            _adminRealmEndpoint = $"{authority}/admin/realms/openksef";
        }
        _adminUsersEndpoint = $"{_adminRealmEndpoint}/users";

        _clientId = configuration["Auth:ServiceAccount:ClientId"] ?? "openksef-api";
    }

    public async Task<CreateUserResult> CreateUserAsync(
        string email, string password, string? firstName, string? lastName)
    {
        if (!string.Equals(_systemConfig.GetValue(SystemConfigKeys.RegistrationAllowed), "true", StringComparison.OrdinalIgnoreCase))
            return RegistrationDisabled();

        var clientSecret = _systemConfig.GetValue(SystemConfigKeys.ApiClientSecret);
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogWarning("Auth:ServiceAccount:ClientSecret not configured: user registration unavailable.");
            return Unavailable();
        }

        try
        {
            var adminToken = await GetServiceAccountTokenAsync(clientSecret);
            if (adminToken is null)
                return Unavailable();

            var policy = await GetRealmPolicyAsync(adminToken);
            if (policy is null)
                return Unavailable();

            if (!policy.Value.RegistrationAllowed)
                return RegistrationDisabled();

            var userPayload = new
            {
                username = email,
                email,
                firstName = firstName ?? "",
                lastName = lastName ?? "",
                enabled = true,
                emailVerified = false,
                requiredActions = policy.Value.VerifyEmail ? new[] { "VERIFY_EMAIL" } : [],
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
                return new CreateUserResult(false, "Konto z tym adresem e-mail już istnieje.", HttpStatusCode.Conflict, "email_taken");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Keycloak user creation failed: {StatusCode} {Body}",
                    response.StatusCode, errorBody);

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return new CreateUserResult(false, "Rejestracja nie powiodła się. Sprawdź podane dane i hasło.",
                        HttpStatusCode.BadRequest, "registration_rejected");
                }

                return Unavailable();
            }

            if (policy.Value.VerifyEmail)
                await SendVerificationEmailAsync(adminToken, response.Headers.Location);

            return new CreateUserResult(true, null, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User creation failed for email {Email}", email);
            return Unavailable();
        }
    }

    public async Task<bool> CanRegisterAsync(string? clientSecret)
    {
        if (string.IsNullOrEmpty(clientSecret))
            return false;

        try
        {
            var adminToken = await GetServiceAccountTokenAsync(clientSecret);
            if (adminToken is null)
                return false;

            var policy = await GetRealmPolicyAsync(adminToken);
            if (policy is not { RegistrationAllowed: true })
                return false;

            var canManageUsers = HasRealmManagementRole(adminToken, "manage-users");
            if (!canManageUsers)
                _logger.LogWarning("Registration stays off: the service account needs realm-management manage-users.");
            return canManageUsers;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Registration probe failed");
            return false;
        }
    }

    public async Task<TwoFactorLookupResult> GetTwoFactorAsync(string email)
    {
        var adminToken = await TryGetServiceAccountTokenAsync();
        if (adminToken is null)
            return new TwoFactorLookupResult(true, false, null);

        var canManage = HasRealmManagementRole(adminToken, "manage-users");
        if (!canManage && !HasRealmManagementRole(adminToken, "view-users"))
            return new TwoFactorLookupResult(false, false, new TwoFactorUserResponse(false, TwoFactorUserResponse.ManageUsersMissing));

        try
        {
            var users = await GetAdminJsonAsync(adminToken, $"{_adminUsersEndpoint}?email={Uri.EscapeDataString(email)}&exact=true");
            if (users is null || users.Value.GetArrayLength() == 0)
                return new TwoFactorLookupResult(false, true, null);

            var user = users.Value[0];
            var userId = user.GetProperty("id").GetString()!;
            var credentials = (await GetAdminJsonAsync(adminToken, $"{_adminUsersEndpoint}/{userId}/credentials"))!.Value.EnumerateArray().ToList();
            var identities = (await GetAdminJsonAsync(adminToken, $"{_adminUsersEndpoint}/{userId}/federated-identity"))!.Value.EnumerateArray();

            var otp = credentials.FirstOrDefault(c => c.GetProperty("type").GetString() == "otp");
            var recoveryCodes = credentials.FirstOrDefault(c => c.GetProperty("type").GetString() == "recovery-authn-codes");
            var status = identities.Any(i => i.GetProperty("identityProvider").GetString() == "google")
                         && credentials.All(c => c.GetProperty("type").GetString() != "password") ? "google"
                : otp.ValueKind == JsonValueKind.Object ? "enabled"
                : "notSet";

            return new TwoFactorLookupResult(false, false, new TwoFactorUserResponse(
                canManage,
                canManage ? null : TwoFactorUserResponse.ManageUsersMissing,
                userId,
                user.TryGetProperty("firstName", out var firstName) ? firstName.GetString() : null,
                user.TryGetProperty("lastName", out var lastName) ? lastName.GetString() : null,
                user.TryGetProperty("email", out var userEmail) ? userEmail.GetString() : null,
                status,
                recoveryCodes.ValueKind == JsonValueKind.Object
                    ? JsonDocument.Parse(recoveryCodes.GetProperty("credentialData").GetString()!).RootElement.GetProperty("remainingCodes").GetInt32()
                    : null,
                otp.ValueKind == JsonValueKind.Object ? DateTimeOffset.FromUnixTimeMilliseconds(otp.GetProperty("createdDate").GetInt64()) : null));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Two-factor lookup failed");
            return new TwoFactorLookupResult(true, false, null);
        }
    }

    public async Task<TwoFactorResetResult> ResetTwoFactorAsync(string userId, string actorUserId, string? actorEmail)
    {
        var adminToken = await TryGetServiceAccountTokenAsync();
        if (adminToken is null)
            return TwoFactorResetResult.KeycloakUnavailable;
        if (!HasRealmManagementRole(adminToken, "manage-users"))
            return TwoFactorResetResult.ResetUnavailable;

        try
        {
            var userUrl = $"{_adminUsersEndpoint}/{Uri.EscapeDataString(userId)}";
            var user = await GetAdminJsonAsync(adminToken, userUrl);
            if (user is null)
                return TwoFactorResetResult.UserNotFound;

            var removed = 0;
            foreach (var credential in (await GetAdminJsonAsync(adminToken, $"{userUrl}/credentials"))!.Value.EnumerateArray()
                         .Where(c => c.GetProperty("type").GetString() is "otp" or "recovery-authn-codes"))
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{userUrl}/credentials/{credential.GetProperty("id").GetString()}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
                using var response = await _httpClient.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.NotFound)
                    response.EnsureSuccessStatusCode();
                removed++;
            }

            if (removed == 0)
                return TwoFactorResetResult.Reset;

            var targetEmail = user.Value.TryGetProperty("email", out var email) ? email.GetString() : null;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                Action = AuditEvent.TwoFactorReset,
                TargetUserId = userId,
                TargetEmail = targetEmail,
            });
            await db.SaveChangesAsync();
            _logger.LogInformation("Two-factor reset of {TargetUserId} ({TargetEmail}) by {ActorUserId} ({ActorEmail}): {Removed} credentials removed",
                userId, targetEmail, actorUserId, actorEmail, removed);
            return TwoFactorResetResult.Reset;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Two-factor reset of {TargetUserId} failed", userId);
            return TwoFactorResetResult.KeycloakUnavailable;
        }
    }

    private static bool HasRealmManagementRole(string adminToken, string role)
    {
        var claims = JsonDocument.Parse(Base64UrlEncoder.Decode(adminToken.Split('.')[1])).RootElement;
        return claims.TryGetProperty("resource_access", out var access)
            && access.TryGetProperty("realm-management", out var realmManagement)
            && realmManagement.GetProperty("roles").EnumerateArray().Any(r => r.GetString() == role);
    }

    private async Task<string?> TryGetServiceAccountTokenAsync()
    {
        var clientSecret = _systemConfig.GetValue(SystemConfigKeys.ApiClientSecret);
        if (string.IsNullOrEmpty(clientSecret))
            return null;
        try
        {
            return await GetServiceAccountTokenAsync(clientSecret);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Service account token request failed");
            return null;
        }
    }

    private async Task<JsonElement?> GetAdminJsonAsync(string adminToken, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static CreateUserResult RegistrationDisabled() => new(
        false, "Rejestracja nowych kont jest wyłączona. Skontaktuj się z administratorem.",
        HttpStatusCode.Forbidden, "registration_disabled");

    private static CreateUserResult Unavailable() => new(
        false, "Rejestracja nie jest teraz dostępna. Skontaktuj się z administratorem.",
        HttpStatusCode.ServiceUnavailable, "registration_unavailable");

    private async Task<(bool RegistrationAllowed, bool VerifyEmail)?> GetRealmPolicyAsync(string adminToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _adminRealmEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Keycloak realm read failed: {StatusCode}. The service account needs view-realm.", response.StatusCode);
            return null;
        }

        // Without view-realm Keycloak answers 200 with a reduced representation, so an absent field must not read as false.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("registrationAllowed", out var registrationAllowed)
            || !json.TryGetProperty("verifyEmail", out var verifyEmail))
        {
            _logger.LogWarning("Keycloak realm read returned no registration policy. The service account needs view-realm.");
            return null;
        }

        return (registrationAllowed.GetBoolean(), verifyEmail.GetBoolean());
    }

    private async Task SendVerificationEmailAsync(string adminToken, Uri? createdUser)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{_adminUsersEndpoint}/{createdUser?.Segments[^1]}/send-verify-email");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Keycloak verification email failed: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak verification email failed");
        }
    }

    private async Task<string?> GetServiceAccountTokenAsync(string clientSecret)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = clientSecret
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
