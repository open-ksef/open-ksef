using System.ComponentModel.DataAnnotations;

namespace OpenKSeF.Api.Models;

public record SetupStatusResponse(bool IsInitialized);

public record SetupAuthenticateRequest(
    [Required] string Username,
    [Required] string Password);

public record SetupAuthenticateResponse(string SetupToken, int ExpiresInSeconds);

public record SetupApplyRequest
{
    [Required] public string ExternalBaseUrl { get; init; } = null!;
    [Required] public string KSeFBaseUrl { get; init; } = null!;
    [Required] public string AdminEmail { get; init; } = null!;
    [Required] public string AdminPassword { get; init; } = null!;
    public string? AdminFirstName { get; init; }
    public string? AdminLastName { get; init; }

    // First tenant/company
    public string? FirstTenantNip { get; init; }
    public string? FirstTenantDisplayName { get; init; }

    // Auth & Email (Keycloak realm config)
    public bool RegistrationAllowed { get; init; } = true;
    public bool VerifyEmail { get; init; }
    public bool LoginWithEmailAllowed { get; init; } = true;
    public bool ResetPasswordAllowed { get; init; } = true;
    public string? PasswordPolicy { get; init; }

    // SMTP
    public SmtpConfig? Smtp { get; init; }

    // Optional integrations
    public string? GoogleClientId { get; init; }
    public string? GoogleClientSecret { get; init; }

    // Push notifications
    public string? PushRelayUrl { get; init; }
    public string? PushRelayApiKey { get; init; }
    public string? FirebaseCredentialsJson { get; init; }

    // Keycloak admin credential change (optional, recommended on first setup)
    public string? NewKeycloakAdminPassword { get; init; }
}

public record SmtpConfig
{
    [Required] public string Host { get; init; } = null!;
    public string Port { get; init; } = "587";
    [Required] public string From { get; init; } = null!;
    public string? FromDisplayName { get; init; }
    public string? ReplyTo { get; init; }
    public bool Starttls { get; init; } = true;
    public bool Ssl { get; init; }
    public bool Auth { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
}

public record SetupApplyResponse(
    bool Success,
    string? EncryptionKey,
    string? ApiClientSecret,
    string? Error);

// --- Settings API (post-setup) ---

public record SettingsAuthRequest
{
    [Required] public string KcAdminUsername { get; init; } = null!;
    [Required] public string KcAdminPassword { get; init; } = null!;
}

public record SettingsResponse
{
    public string? ExternalBaseUrl { get; init; }
    public string? KSeFEnvironment { get; init; }
    public bool KSeFEnvironmentLocked { get; init; }
    public string? KSeFEnvironmentLockReason { get; init; }

    public bool RegistrationAllowed { get; init; }
    public bool VerifyEmail { get; init; }
    public bool LoginWithEmailAllowed { get; init; }
    public bool ResetPasswordAllowed { get; init; }
    public string? PasswordPolicy { get; init; }

    public SmtpConfig? Smtp { get; init; }

    public string? GoogleClientId { get; init; }
    public bool GoogleConfigured { get; init; }
    public string? PushRelayUrl { get; init; }
    public string? PushRelayApiKey { get; init; }
    public string? PushRelayInstanceId { get; init; }
    public bool FirebaseConfigured { get; init; }
}

public record SettingsUpdateRequest
{
    [Required] public string KcAdminUsername { get; init; } = null!;
    [Required] public string KcAdminPassword { get; init; } = null!;

    public string? ExternalBaseUrl { get; init; }
    public string? KSeFEnvironment { get; init; }
    public bool? RegistrationAllowed { get; init; }
    public bool? VerifyEmail { get; init; }
    public bool? LoginWithEmailAllowed { get; init; }
    public bool? ResetPasswordAllowed { get; init; }
    public string? PasswordPolicy { get; init; }
    public SmtpConfig? Smtp { get; init; }
    public bool ClearSmtp { get; init; }
    public string? GoogleClientId { get; init; }
    public string? GoogleClientSecret { get; init; }
    public string? PushRelayUrl { get; init; }
    public string? PushRelayApiKey { get; init; }
    public bool ReRegisterRelay { get; init; }
    public string? FirebaseCredentialsJson { get; init; }
    public bool ConfirmCredentialWipe { get; init; }
}

public record SettingsUpdateResponse(bool Success, string? Error);
