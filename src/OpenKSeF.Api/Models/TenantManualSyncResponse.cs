namespace OpenKSeF.Api.Models;

public record TenantManualSyncResponse(
    Guid TenantId,
    int FetchedInvoices,
    int NewInvoices,
    DateTime SyncedAtUtc);
