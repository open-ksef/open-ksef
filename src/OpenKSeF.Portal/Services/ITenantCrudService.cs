using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Portal.Services;

public interface ITenantCrudService
{
    Task<IReadOnlyList<Tenant>> ListAsync();
    Task<TenantFormModel?> GetAsync(Guid id);
    Task<TenantOperationResult> CreateAsync(TenantFormModel model);
    Task<TenantOperationResult> UpdateAsync(Guid id, TenantFormModel model);
    Task<TenantOperationResult> DeleteAsync(Guid id);
}
