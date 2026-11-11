using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

/// <summary>Runs manual syncs outside the request, one job per tenant; a second start while one runs attaches to it.</summary>
public sealed class ManualSyncJobs
{
    private const int MaxConcurrentJobs = 4;

    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly SemaphoreSlim _slots = new(MaxConcurrentJobs);
    private readonly object _startLock = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CancellationToken _stopping;
    private readonly TimeSpan _hold;
    private readonly TimeProvider _time;
    private readonly ILogger<ManualSyncJobs> _logger;

    public ManualSyncJobs(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IOptions<TenantSyncOptions> syncOptions,
        TimeProvider time,
        ILogger<ManualSyncJobs> logger)
    {
        _scopeFactory = scopeFactory;
        _stopping = lifetime.ApplicationStopping;
        _hold = syncOptions.Value.ManualSyncHold;
        _time = time;
        _logger = logger;
    }

    public Job Start(Guid tenantId, string userId, bool fullResync)
    {
        lock (_startLock)
        {
            if (_jobs.TryGetValue(tenantId, out var current) && !current.Completion.IsCompleted)
                return current;

            var job = new Job(fullResync, _time.GetUtcNow().UtcDateTime);
            job.Completion = Task.Run(() => RunAsync(job, tenantId, userId));
            _jobs[tenantId] = job;
            return job;
        }
    }

    public Job? Find(Guid tenantId) => _jobs.GetValueOrDefault(tenantId);

    /// <returns>The result, or null when the job is still running after the hold.</returns>
    public async Task<TenantSyncResult?> WaitAsync(Job job, CancellationToken cancellationToken)
    {
        try
        {
            return await job.Completion.WaitAsync(_hold, _time, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<TenantSyncResult> RunAsync(Job job, Guid tenantId, string userId)
    {
        try
        {
            await _slots.WaitAsync(_stopping);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<ITenantSyncService>()
                    .SyncTenantAsync(tenantId, userId, job.FullResync, _stopping, () => job.Running = true);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual sync job failed for tenant {TenantId}", tenantId);
            return new TenantSyncResult(tenantId, string.Empty, TenantSyncOutcome.Failed, ErrorMessage: "Synchronization failed. Details are in the server log.");
        }
        finally
        {
            job.FinishedAtUtc = _time.GetUtcNow().UtcDateTime;
        }
    }

    public sealed class Job(bool fullResync, DateTime startedAtUtc)
    {
        public bool FullResync { get; } = fullResync;

        public DateTime StartedAtUtc { get; } = startedAtUtc;

        public volatile bool Running;

        public DateTime? FinishedAtUtc { get; set; }

        public Task<TenantSyncResult> Completion { get; set; } = null!;
    }
}
