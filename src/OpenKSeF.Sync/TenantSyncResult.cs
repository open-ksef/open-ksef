namespace OpenKSeF.Sync;

public sealed record TenantSyncResult(
    Guid TenantId,
    string Nip,
    TenantSyncOutcome Outcome,
    int FetchedInvoices = 0,
    int NewInvoices = 0,
    DateTime? SyncedAtUtc = null,
    string? ErrorMessage = null,
    int? ErrorStatusCode = null);
