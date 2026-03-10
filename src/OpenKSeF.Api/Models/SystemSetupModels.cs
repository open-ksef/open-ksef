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
