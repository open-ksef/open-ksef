using System.ComponentModel.DataAnnotations;

namespace OpenKSeF.Domain.DTOs;

public record CreateTenantRequest(
    [param: Required]
    [param: RegularExpression(@"^[\d\-]{10,13}$", ErrorMessage = "NIP must be 10 digits, optionally with dashes.")]
    string Nip,
    string? DisplayName,
    [param: EmailAddress]
    string? NotificationEmail);

public record UpdateTenantRequest(
    string? DisplayName,
    [param: EmailAddress]
    string? NotificationEmail);

public record TenantResponse(
    Guid Id,
    string Nip,
    string? DisplayName,
    string? NotificationEmail,
    DateTime CreatedAt);
