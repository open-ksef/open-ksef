namespace OpenKSeF.Sync;

public interface ITenantSyncService
{
    Task<IReadOnlyList<TenantSyncResult>> SyncAllTenantsAsync(CancellationToken cancellationToken = default);

    Task<TenantSyncResult> SyncTenantAsync(
        Guid tenantId,
        string? userId = null,
        bool forceFullResync = false,
        CancellationToken cancellationToken = default);
}
