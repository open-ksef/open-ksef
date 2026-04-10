using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Retrieves a resolved <see cref="IPolicySnapshot"/> for a given tenant.
/// Implementations live in the infrastructure layer and may load from database or configuration.
/// </summary>
public interface IPolicyProvider
{
    Task<IPolicySnapshot> GetForTenantAsync(TenantId tenantId, CancellationToken ct);
}
