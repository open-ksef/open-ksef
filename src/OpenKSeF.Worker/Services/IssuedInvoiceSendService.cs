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

        var lastErrors = new Dictionary<string, (string Message, DateTimeOffset LogAgainAt)>();

        async Task RunAsync(string step, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var now = timeProvider.GetUtcNow();
                if (lastErrors.TryGetValue(step, out var last) && ex.Message == last.Message && now < last.LogAgainAt)
                    return;

                logger.LogError(ex, "Unhandled error in issued invoice {Step}. The same error repeats in the log at most every {Minutes} min", step, RepeatedErrorLogInterval.TotalMinutes);
                lastErrors[step] = (ex.Message, now + RepeatedErrorLogInterval);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync("send cycle", async () =>
            {
                var environment = await runtimeConfig.RefreshAsync(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IssuedInvoiceSendProcessor>().ProcessAsync(environment, stoppingToken);
            });

            await RunAsync("escalation", async () =>
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IssuedInvoiceEscalationService>().EscalateAsync(stuckAfter, stoppingToken);
            });

            await Task.Delay(interval, stoppingToken);
        }
    }
}
