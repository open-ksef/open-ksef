namespace OpenKSeF.Portal.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<TenantDashboardSummary>> GetTenantOverviewAsync();
}
