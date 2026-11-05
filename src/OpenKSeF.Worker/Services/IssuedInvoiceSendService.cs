using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Worker.Services;

/// <summary>Polls the issued-invoice outbox every Issuing:PollSeconds and runs one send cycle.</summary>
public sealed class IssuedInvoiceSendService(
    IServiceScopeFactory scopeFactory,
    KSeFRuntimeConfig runtimeConfig,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<IssuedInvoiceSendService> logger) : BackgroundService
{
    private static readonly TimeSpan RepeatedErrorLogInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Issuing:PollSeconds", 5));
        var stuckAfter = TimeSpan.FromHours(configuration.GetValue("Issuing:StuckAfterHours", 24));
        logger.LogInformation("IssuedInvoiceSendService started. Poll: {Seconds}s", interval.TotalSeconds);

        await MigrationHelper.WaitForMigrationsAsync(scopeFactory, logger, stoppingToken);

        string? lastError = null;
        var logErrorAgainAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var environment = await runtimeConfig.RefreshAsync(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IssuedInvoiceSendProcessor>().ProcessAsync(environment, stoppingToken);

                using var escalationScope = scopeFactory.CreateScope();
                await escalationScope.ServiceProvider.GetRequiredService<IssuedInvoiceEscalationService>().EscalateAsync(stuckAfter, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var now = timeProvider.GetUtcNow();
                if (ex.Message != lastError || now >= logErrorAgainAt)
                {
                    logger.LogError(ex, "Unhandled error in issued invoice send cycle. The same error repeats in the log at most every {Minutes} min", RepeatedErrorLogInterval.TotalMinutes);
                    lastError = ex.Message;
                    logErrorAgainAt = now + RepeatedErrorLogInterval;
                }
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
