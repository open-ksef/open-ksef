using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Portal.Services;

public sealed class DeviceTokenOverviewRow
{
    public Guid Id { get; init; }
    public Platform Platform { get; init; }
    public Guid? TenantId { get; init; }
    public string TokenMasked { get; init; } = string.Empty;
    public DateTime RegisteredAt { get; init; }
    public DateTime LastSeenAt { get; init; }
}
