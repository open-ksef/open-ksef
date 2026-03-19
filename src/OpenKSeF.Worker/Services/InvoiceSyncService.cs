using KSeF.Client.DI;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Worker.Services;

public sealed class InvoiceSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemConfigService _systemConfig;
    private readonly KSeFClientOptions _ksefOptions;
    private readonly SyncOptions _syncOptions;
    private readonly ILogger<InvoiceSyncService> _logger;

    public InvoiceSyncService(
        IServiceScopeFactory scopeFactory,
        ISystemConfigService systemConfig,
        KSeFClientOptions ksefOptions,
        IOptions<SyncOptions> syncOptions,
        ILogger<InvoiceSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _systemConfig = systemConfig;
        _ksefOptions = ksefOptions;
        _syncOptions = syncOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InvoiceSyncService started. Interval: {Hours}h", _syncOptions.IntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            });

            _logger.LogInformation("Starting sync cycle {CorrelationId}", correlationId);

            try
            {
                await RefreshRuntimeConfigAsync();
                await SyncAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in sync cycle {CorrelationId}", correlationId);
            }

            _logger.LogInformation("Sync cycle complete. Next run in {Hours}h", _syncOptions.IntervalHours);
            await Task.Delay(TimeSpan.FromHours(_syncOptions.IntervalHours), stoppingToken);
        }
    }

    private async Task RefreshRuntimeConfigAsync()
    {
        await _systemConfig.RefreshCacheAsync();

        var ksefEnv = _systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
            ?? _systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl);

        if (!string.IsNullOrEmpty(ksefEnv))
        {
            var resolvedUrl = DependencyInjection.ResolveKSeFEnvironment(ksefEnv);
            if (_ksefOptions.BaseUrl != resolvedUrl)
            {
                _logger.LogInformation("KSeF environment changed to {Env} ({Url})", ksefEnv, resolvedUrl);
                _ksefOptions.BaseUrl = resolvedUrl;
            }
        }
    }

    private async Task SyncAllTenantsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tenantSyncService = scope.ServiceProvider.GetRequiredService<ITenantSyncService>();
        var results = await tenantSyncService.SyncAllTenantsAsync(cancellationToken);

        var successCount = results.Count(r => r.Outcome == TenantSyncOutcome.Success);
        var failedCount = results.Count(r => r.Outcome == TenantSyncOutcome.Failed);
        _logger.LogInformation("Sync cycle processed {Total} tenants. Success: {Success}, Failed: {Failed}",
            results.Count, successCount, failedCount);

        foreach (var result in results.Where(r => r.Outcome != TenantSyncOutcome.Success))
        {
            _logger.LogWarning("Tenant sync outcome {Outcome} for tenant {TenantId} (NIP: {Nip}). Error: {Error}",
                result.Outcome, result.TenantId, result.Nip, result.ErrorMessage);
        }
    }
}
