using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Worker.Services;

/// <summary>Reloads system config and points the shared KSeF client at the configured environment; used by every Worker loop.</summary>
public sealed class KSeFRuntimeConfig(
    ISystemConfigService systemConfig,
    KSeFClientOptions ksefOptions,
    ICryptographyService cryptography,
    ILogger<KSeFRuntimeConfig> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await systemConfig.RefreshCacheAsync();

            var ksefEnv = systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment)
                ?? systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl);

            if (!string.IsNullOrEmpty(ksefEnv))
            {
                var resolvedUrl = DependencyInjection.ResolveKSeFEnvironment(ksefEnv);
                if (ksefOptions.BaseUrl != resolvedUrl)
                {
                    logger.LogInformation("KSeF environment changed to {Env} ({Url})", ksefEnv, resolvedUrl);
                    ksefOptions.BaseUrl = resolvedUrl;
                    await cryptography.ForceRefreshAsync(cancellationToken);
                }
            }

            // Startup warmup runs in the background and loses the race with the first cycle.
            await cryptography.WarmupAsync(cancellationToken);
            return DependencyInjection.NormalizeKSeFEnvironmentKey(ksefEnv);
        }
        finally
        {
            _gate.Release();
        }
    }
}
