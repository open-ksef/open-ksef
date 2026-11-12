using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Data;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

/// <summary>Degraded when a tenant with a KSeF credential has not synced for three Worker intervals, e.g. the Worker is stopped. Logs only when the status changes.</summary>
public sealed class SyncFreshnessHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantSyncOptions> options,
    TimeProvider time,
    ILogger<SyncFreshnessHealthCheck> logger) : IHealthCheck
{
    private int _lastStatus = (int)HealthStatus.Healthy;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = TimeSpan.FromHours(3 * options.Value.IntervalHours);
        var cutoff = time.GetUtcNow().UtcDateTime - threshold;

        using var scope = scopeFactory.CreateScope();
        var stale = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().KSeFCredentials
            .Where(c => (c.Tenant.SyncState!.LastSyncedAt ?? c.CreatedAt) < cutoff)
            .Select(c => c.TenantId)
            .Distinct()
            .CountAsync(cancellationToken);

        var result = stale == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{stale} tenant(s) not synced for over {threshold.TotalHours:0}h");

        if (Interlocked.Exchange(ref _lastStatus, (int)result.Status) != (int)result.Status)
        {
            if (result.Status == HealthStatus.Healthy)
                logger.LogInformation("Sync freshness recovered: all tenants synced within {Hours:0}h", threshold.TotalHours);
            else
                logger.LogWarning("Sync freshness degraded: {Description}", result.Description);
        }

        return result;
    }
}
