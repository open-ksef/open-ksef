using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Models;

public record DeviceTokenResponse(
    Guid Id,
    string Token,
    Platform Platform,
    Guid? TenantId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
