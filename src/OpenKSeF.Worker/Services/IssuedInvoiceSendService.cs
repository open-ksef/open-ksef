using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Worker.Services;

/// <summary>Polls the issued-invoice outbox every Issuing:PollSeconds and runs one send cycle.</summary>
public sealed class IssuedInvoiceSendService(
    IServiceScopeFactory scopeFactory,
    KSeFRuntimeConfig runtimeConfig,
    IConfiguration configuration,
    ILogger<IssuedInvoiceSendService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Issuing:PollSeconds", 5));
        logger.LogInformation("IssuedInvoiceSendService started. Poll: {Seconds}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var environment = await runtimeConfig.RefreshAsync(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IssuedInvoiceSendProcessor>().ProcessAsync(environment, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in issued invoice send cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
