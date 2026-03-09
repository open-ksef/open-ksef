namespace OpenKSeF.Domain.Entities;

public class SyncState
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? Watermark { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
