using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OpenKSeF.Domain.DTOs;

public record CreateTenantRequest(
    [param: Required]
    [param: RegularExpression(@"^[\d\-]{10,13}$", ErrorMessage = "NIP must be 10 digits, optionally with dashes.")]
    string Nip,
    string? DisplayName,
    [param: EmailAddress]
    string? NotificationEmail);

public record UpdateTenantRequest(string? DisplayName)
{
    private readonly string? _notificationEmail;

    [EmailAddress]
    public string? NotificationEmail
    {
        get => _notificationEmail;
        init
        {
            _notificationEmail = string.IsNullOrWhiteSpace(value) ? null : value;
            NotificationEmailSet = true;
        }
    }

    [JsonIgnore]
    public bool NotificationEmailSet { get; private init; }
}

public record TenantResponse(
    Guid Id,
    string Nip,
    string? DisplayName,
    string? NotificationEmail,
    DateTime CreatedAt);
