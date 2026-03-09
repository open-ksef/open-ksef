namespace OpenKSeF.Portal.Services;

public interface ITenantResolver
{
    string? GetCurrentUserId();
    Task<List<Guid>> GetUserTenantIdsAsync();
    Task<Guid?> GetCurrentTenantIdAsync();
    Task<bool> HasAccessToTenantAsync(Guid tenantId);
}
