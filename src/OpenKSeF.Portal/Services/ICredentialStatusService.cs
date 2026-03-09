namespace OpenKSeF.Portal.Services;

public interface ICredentialStatusService
{
    Task<IReadOnlyList<TenantCredentialStatusRow>> GetStatusesAsync();
}
