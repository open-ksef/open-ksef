using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Data;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

/// <summary>Degraded when a tenant with a KSeF credential has not synced for three Worker intervals, e.g. the Worker is stopped.</summary>
public sealed class SyncFreshnessHealthCheck(ApplicationDbContext db, IOptions<TenantSyncOptions> options, TimeProvider time) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = TimeSpan.FromHours(3 * options.Value.IntervalHours);
        var cutoff = time.GetUtcNow().UtcDateTime - threshold;

        var stale = await db.KSeFCredentials
            .Where(c => (c.Tenant.SyncState!.LastSyncedAt ?? c.CreatedAt) < cutoff)
            .Select(c => c.TenantId)
            .Distinct()
            .CountAsync(cancellationToken);

        return stale == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{stale} tenant(s) not synced for over {threshold.TotalHours:0}h");
    }
}
