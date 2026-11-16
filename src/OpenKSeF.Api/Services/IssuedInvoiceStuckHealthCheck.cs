using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.Services;

/// <summary>Degraded while the Worker has escalated an issued invoice as stuck; the operator's signal for docs/issuing-stuck-runbook.md. Logs only when the status changes.</summary>
public sealed class IssuedInvoiceStuckHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<IssuedInvoiceStuckHealthCheck> logger) : IHealthCheck
{
    private int _lastStatus = (int)HealthStatus.Healthy;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;

        using var scope = scopeFactory.CreateScope();
        // A future EscalatedAt parks an invoice kept after an account deletion so it never escalates; it is not stuck.
        var stuck = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().IssuedInvoices
            .CountAsync(i => i.EscalatedAt != null && i.EscalatedAt <= now, cancellationToken);

        var result = stuck == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{stuck} issued invoice(s) stuck in KSeF, see docs/issuing-stuck-runbook.md");

        if (Interlocked.Exchange(ref _lastStatus, (int)result.Status) != (int)result.Status)
        {
            if (result.Status == HealthStatus.Healthy)
                logger.LogInformation("Issued invoices: none stuck");
            else
                logger.LogWarning("Issued invoices degraded: {Description}", result.Description);
        }

        return result;
    }
}
