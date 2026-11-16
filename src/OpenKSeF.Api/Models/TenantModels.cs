using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Models;

public record CreateTenantRequest(
    [param: Required]
    [param: RegularExpression(@"^[\d\-]{10,13}$", ErrorMessage = "NIP must be 10 digits, optionally with dashes.")]
    string Nip,
    [param: MaxLength(200, ErrorMessage = "Nazwa może mieć najwyżej 200 znaków.")]
    string? DisplayName,
    [param: EmailAddress]
    string? NotificationEmail)
{
    public string? NotificationEmail { get; init; } = string.IsNullOrWhiteSpace(NotificationEmail) ? null : NotificationEmail.Trim();
}

public record UpdateTenantRequest(
    [param: MaxLength(200, ErrorMessage = "Nazwa może mieć najwyżej 200 znaków.")]
    string? DisplayName)
{
    private readonly string? _notificationEmail;

    [EmailAddress]
    public string? NotificationEmail
    {
        get => _notificationEmail;
        init
        {
            _notificationEmail = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
    DateTime CreatedAt,
    string Role = nameof(TenantRole.Owner),
    IReadOnlyList<string>? Permissions = null,
    bool NotificationsEnabled = true)
{
    public static TenantResponse From(Tenant tenant, TenantMember member) => new(
        tenant.Id, tenant.Nip, tenant.DisplayName, tenant.NotificationEmail, tenant.CreatedAt,
        member.Role.ToString(), TenantPermissions.For(member.Role).Order(StringComparer.Ordinal).ToList(),
        member.NotifyNewInvoices);
}

public record SetMemberNotificationsRequest([param: Required] bool? Enabled);

public record TenantMemberResponse(string UserId, string? DisplayName, string? Email, string Role, DateTime CreatedAt);

public record ChangeMemberRoleRequest(
    [param: Required]
    [param: AllowedValues(nameof(TenantRole.Owner), nameof(TenantRole.Administrator), nameof(TenantRole.Accountant), nameof(TenantRole.Employee), ErrorMessage = "Nieznana rola.")]
    string? Role);
