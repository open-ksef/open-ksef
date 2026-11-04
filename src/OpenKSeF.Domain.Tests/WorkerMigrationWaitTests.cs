using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using OpenKSeF.Worker;
using OpenKSeF.Worker.Services;

namespace OpenKSeF.Domain.Tests;

public sealed class WorkerMigrationWaitTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"openksef-migration-wait-{Guid.NewGuid():N}.db");
    private readonly ITenantSyncService _sync = Substitute.For<ITenantSyncService>();

    private ServiceProvider Services() => new ServiceCollection()
        .AddDbContext<ApplicationDbContext>(o => o
            .UseSqlite($"Data Source={_dbPath}")
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)))
        .AddSingleton(_sync)
        .BuildServiceProvider();

    private static KSeFRuntimeConfig RuntimeConfig() => new(
        Substitute.For<ISystemConfigService>(),
        new KSeFClientOptions(),
        Substitute.For<ICryptographyService>(),
        NullLogger<KSeFRuntimeConfig>.Instance);

    private static async Task MigrateAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private InvoiceSyncService SyncLoop(ServiceProvider services, ILogger<InvoiceSyncService> logger) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        RuntimeConfig(),
        Options.Create(new SyncOptions { IntervalHours = 4 }),
        logger);

    [Fact]
    public async Task Helper_ReturnsOnlyAfterMigrationsApplied_WithoutError()
    {
        await using var services = Services();
        var logger = new ListLogger<WorkerMigrationWaitTests>();

        var wait = MigrationHelper.WaitForMigrationsAsync(
            services.GetRequiredService<IServiceScopeFactory>(), logger, CancellationToken.None, TimeSpan.FromMilliseconds(20));
        await Task.Delay(200);
        Assert.False(wait.IsCompleted);

        await MigrateAsync(services);

        await wait.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(logger.Levels, l => l >= LogLevel.Error);
        Assert.Contains(LogLevel.Information, logger.Levels);
    }

    [Fact]
    public async Task Helper_DatabaseUnreachable_RetriesWithWarning_ThenStopsOnCancel()
    {
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseSqlite("Data Source=/no/such/dir/at/all/x.db"))
            .BuildServiceProvider();
        await using var _ = services;
        var logger = new ListLogger<WorkerMigrationWaitTests>();
        using var cts = new CancellationTokenSource();

        var wait = MigrationHelper.WaitForMigrationsAsync(
            services.GetRequiredService<IServiceScopeFactory>(), logger, cts.Token, TimeSpan.FromMilliseconds(20));
        await Task.Delay(300);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.DoesNotContain(logger.Levels, l => l >= LogLevel.Error);
    }

    [Fact]
    public async Task SyncLoop_PendingMigrations_RunsNoCycle_LogsNoError()
    {
        await using var services = Services();
        var logger = new ListLogger<InvoiceSyncService>();
        var loop = SyncLoop(services, logger);

        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await loop.StopAsync(CancellationToken.None);

        await _sync.DidNotReceive().SyncAllTenantsAsync(Arg.Any<CancellationToken>());
        Assert.DoesNotContain(logger.Levels, l => l >= LogLevel.Error);
    }

    [Fact]
    public async Task SyncLoop_NoPendingMigrations_RunsCycle()
    {
        await using var services = Services();
        await MigrateAsync(services);
        var cycleRan = new TaskCompletionSource();
        _sync.SyncAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cycleRan.TrySetResult();
            return Task.FromResult<IReadOnlyList<TenantSyncResult>>([]);
        });
        var loop = SyncLoop(services, NullLogger<InvoiceSyncService>.Instance);

        await loop.StartAsync(CancellationToken.None);
        await cycleRan.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SendLoop_PendingMigrations_RunsNoCycle_LogsNoError()
    {
        await using var services = Services();
        var logger = new ListLogger<IssuedInvoiceSendService>();
        var loop = new IssuedInvoiceSendService(
            services.GetRequiredService<IServiceScopeFactory>(),
            RuntimeConfig(),
            new ConfigurationBuilder().Build(),
            logger);

        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await loop.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Levels, l => l >= LogLevel.Error);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<LogLevel> _levels = [];

        public IReadOnlyList<LogLevel> Levels
        {
            get
            {
                lock (_levels)
                    return _levels.ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_levels)
                _levels.Add(logLevel);
        }
    }
}
