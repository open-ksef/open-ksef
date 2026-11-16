using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Entities;

/// <summary>E-mail invite to a company; only the SHA-256 of the link token is stored, and a pending invite past ExpiresAt is expired.</summary>
public class TenantInvite
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Email { get; set; }
    public TenantRole Role { get; set; }
    public required string TokenHash { get; set; }
    public TenantInviteStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required string InvitedByUserId { get; set; }
    public string? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }

    public void Accept(string userId, DateTime now)
    {
        Status = TenantInviteStatus.Accepted;
        AcceptedByUserId = userId;
        AcceptedAt = now;
    }
}
