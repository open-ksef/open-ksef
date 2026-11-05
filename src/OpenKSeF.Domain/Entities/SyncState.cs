namespace OpenKSeF.Domain.Entities;

public class SyncState
{
    private static readonly TimeSpan IssuedRecentRun = TimeSpan.FromMinutes(15);

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? Watermark { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }
    public DateTime? IssuedContinuationAt { get; set; }
    public DateTime? ReceivedContinuationAt { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public bool IssuedFetchIsRecent(DateTime at) => IssuedContinuationAt > at - IssuedRecentRun;

    // A run that finishes, or skips, leaves the continuation inside this window, so an older one means it stopped early.
    public bool IssuedFetchIncomplete() => LastSyncedAt is { } lastRun && !IssuedFetchIsRecent(lastRun);
}
