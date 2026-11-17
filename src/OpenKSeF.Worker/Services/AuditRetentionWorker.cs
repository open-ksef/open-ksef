using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Worker.Services;

/// <summary>Runs the unlinked audit event purge once a day.</summary>
public sealed class AuditRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MigrationHelper.WaitForMigrationsAsync(scopeFactory, logger, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<AuditRetentionService>().PurgeUnlinkedAsync(stoppingToken);
                logger.LogInformation("Audit retention deleted {Count} unlinked events older than {Months} months", deleted, AuditRetentionService.RetentionMonths);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Audit retention failed. Next run in {Hours}h", Interval.TotalHours);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
