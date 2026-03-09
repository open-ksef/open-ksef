using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Portal.Services;

public sealed class DashboardService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver,
    TimeProvider? timeProvider = null) : IDashboardService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<TenantDashboardSummary>> GetTenantOverviewAsync()
    {
        var tenantIds = await tenantResolver.GetUserTenantIdsAsync();
        if (tenantIds.Count == 0)
        {
            return [];
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var last7DaysThreshold = now.AddDays(-7);
        var last30DaysThreshold = now.AddDays(-30);

        var tenants = await dbContext.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .OrderBy(t => t.DisplayName ?? t.Nip)
            .Select(t => new
            {
                t.Id,
                t.Nip,
                t.DisplayName
            })
            .ToListAsync();

        var syncByTenant = await dbContext.SyncStates
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId);

        var countsByTenant = await dbContext.InvoiceHeaders
            .Where(i => tenantIds.Contains(i.TenantId))
            .GroupBy(i => i.TenantId)
            .Select(group => new
            {
                TenantId = group.Key,
                Total = group.Count(),
                Last7Days = group.Count(i => i.IssueDate >= last7DaysThreshold),
                Last30Days = group.Count(i => i.IssueDate >= last30DaysThreshold)
            })
            .ToDictionaryAsync(g => g.TenantId);

        return tenants.Select(tenant =>
        {
            syncByTenant.TryGetValue(tenant.Id, out var syncState);
            countsByTenant.TryGetValue(tenant.Id, out var counts);

            var lastSuccessfulSync = syncState?.LastSuccessfulSync;
            var status = ResolveSyncStatus(lastSuccessfulSync, now);

            return new TenantDashboardSummary
            {
                TenantId = tenant.Id,
                Nip = tenant.Nip,
                DisplayName = tenant.DisplayName,
                LastSyncedAt = syncState?.LastSyncedAt,
                LastSuccessfulSync = lastSuccessfulSync,
                TotalInvoices = counts?.Total ?? 0,
                InvoicesLast7Days = counts?.Last7Days ?? 0,
                InvoicesLast30Days = counts?.Last30Days ?? 0,
                SyncStatus = status
            };
        }).ToList();
    }

    private static SyncHealthStatus ResolveSyncStatus(DateTime? lastSuccessfulSync, DateTime now)
    {
        if (!lastSuccessfulSync.HasValue)
        {
            return SyncHealthStatus.Error;
        }

        var age = now - lastSuccessfulSync.Value;
        if (age <= TimeSpan.FromHours(24))
        {
            return SyncHealthStatus.Success;
        }

        if (age <= TimeSpan.FromHours(72))
        {
            return SyncHealthStatus.Warning;
        }

        return SyncHealthStatus.Error;
    }
}
