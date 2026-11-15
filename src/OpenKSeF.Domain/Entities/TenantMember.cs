using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Entities;

public class TenantMember
{
    public Guid TenantId { get; set; }
    public required string UserId { get; set; }
    public TenantRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}
