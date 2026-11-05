namespace OpenKSeF.Sync;

public sealed record TenantSyncResult(
    Guid TenantId,
    string Nip,
    TenantSyncOutcome Outcome,
    int FetchedInvoices = 0,
    int NewInvoices = 0,
    DateTime? SyncedAtUtc = null,
    string? ErrorMessage = null,
    int? ErrorStatusCode = null,
    int QueuedDownloads = 0,
    int ErroredDownloads = 0)
{
    // Portal builds before queuedDownloads read this as "run incomplete", so it must keep counting queued rows.
    public int FailedDownloads => QueuedDownloads + ErroredDownloads;
}
