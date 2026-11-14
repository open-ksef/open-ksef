using System.Text.Json.Serialization;

namespace OpenKSeF.Api.Models;

public record TwoFactorUserResponse(
    [property: JsonPropertyName("resetAvailable")] bool ResetAvailable,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("userId")] string? UserId = null,
    [property: JsonPropertyName("firstName")] string? FirstName = null,
    [property: JsonPropertyName("lastName")] string? LastName = null,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("recoveryCodesLeft")] int? RecoveryCodesLeft = null,
    [property: JsonPropertyName("enabledAt")] DateTimeOffset? EnabledAt = null)
{
    public const string ManageUsersMissing = "manage_users_missing";
}
