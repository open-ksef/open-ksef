namespace OpenKSeF.Api.Models;

public record TenantManualSyncResponse(
    Guid TenantId,
    int FetchedInvoices,
    int NewInvoices,
    DateTime SyncedAtUtc,
    int FailedDownloads = 0,
    int QueuedDownloads = 0,
    int ErroredDownloads = 0,
    int IssuedFetchedInvoices = 0,
    int IssuedNewInvoices = 0);
