using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Entities;

public class TenantMember
{
    public Guid TenantId { get; set; }
    public required string UserId { get; set; }
    public TenantRole Role { get; set; }
    public bool NotifyNewInvoices { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
