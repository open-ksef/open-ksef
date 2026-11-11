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
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using OpenKSeF.Sync.Issuing;
using OpenKSeF.Worker;
using OpenKSeF.Worker.Services;

namespace OpenKSeF.Domain.Tests;

public sealed class WorkerMigrationWaitTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"openksef-migration-wait-{Guid.NewGuid():N}.db");
    private readonly ITenantSyncService _sync = Substitute.For<ITenantSyncService>();

    private ServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o
                .UseSqlite($"Data Source={_dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)))
            .AddSingleton(_sync)
            .AddScoped(_ => Escalation());
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // In memory: the escalation query reads the Postgres xmin column, which the SQLite schema lacks.
    private static IssuedInvoiceEscalationService Escalation() => new(
        new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        Substitute.For<INotificationService>(),
        TimeProvider.System,
        NullLogger<IssuedInvoiceEscalationService>.Instance);

    private static KSeFRuntimeConfig RuntimeConfig(string? environment = null)
    {
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment).Returns(environment);
        return new KSeFRuntimeConfig(
            systemConfig,
            new KSeFClientOptions(),
            Substitute.For<ICryptographyService>(),
            NullLogger<KSeFRuntimeConfig>.Instance);
    }

    private static async Task MigrateAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private InvoiceSyncService SyncLoop(ServiceProvider services, ILogger<InvoiceSyncService> logger, string? environment = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        RuntimeConfig(environment),
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
    public async Task Helper_LongWait_LogsWarning()
    {
        await using var services = Services();
        var logger = new ListLogger<WorkerMigrationWaitTests>();
        using var cts = new CancellationTokenSource();

        var wait = MigrationHelper.WaitForMigrationsAsync(
            services.GetRequiredService<IServiceScopeFactory>(), logger, cts.Token, TimeSpan.FromMilliseconds(20), warnAfter: TimeSpan.Zero);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!logger.Levels.Contains(LogLevel.Warning) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task Helper_TimeoutWithoutCancelledToken_RetriesInsteadOfStoppingTheHost()
    {
        var timeouts = 0;
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ =>
        {
            Interlocked.Increment(ref timeouts);
            throw new TaskCanceledException("Simulated driver timeout");
        });
        using var cts = new CancellationTokenSource();

        var wait = MigrationHelper.WaitForMigrationsAsync(
            scopeFactory, new ListLogger<WorkerMigrationWaitTests>(), cts.Token, TimeSpan.FromMilliseconds(20));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref timeouts) < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.False(wait.IsCompleted);
        Assert.True(Volatile.Read(ref timeouts) >= 2);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task Helper_OtherExceptionAfterCancel_StopsWithCancellation()
    {
        using var cts = new CancellationTokenSource();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ =>
        {
            cts.Cancel();
            throw new InvalidOperationException("Connection closed during shutdown");
        });
        var logger = new ListLogger<WorkerMigrationWaitTests>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MigrationHelper.WaitForMigrationsAsync(
            scopeFactory, logger, cts.Token, TimeSpan.FromMilliseconds(20)));

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
        var logger = new ListLogger<InvoiceSyncService>();
        var loop = SyncLoop(services, logger);

        await loop.StartAsync(CancellationToken.None);
        await cycleRan.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await loop.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Messages, m => m.StartsWith("Sync cycle complete"));
    }

    [Fact]
    public async Task SyncLoop_StoreFailed_LogsOutcomePerTenant()
    {
        await using var services = Services();
        await MigrateAsync(services);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _sync.SyncAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<TenantSyncResult>>(
        [
            new TenantSyncResult(tenantA, "1111111111", TenantSyncOutcome.StoreFailed, ErrorMessage: "Invoice data could not be stored."),
            new TenantSyncResult(tenantB, "2222222222", TenantSyncOutcome.StoreFailed, ErrorMessage: "Invoice data could not be stored."),
            new TenantSyncResult(Guid.NewGuid(), "3333333333", TenantSyncOutcome.Success),
            new TenantSyncResult(Guid.NewGuid(), "4444444444", TenantSyncOutcome.Busy, ErrorMessage: TenantSyncService.BusyMessage)
        ]));
        var logger = new ListLogger<InvoiceSyncService>();
        var loop = SyncLoop(services, logger);

        await loop.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!logger.Messages.Any(m => m.StartsWith("Sync cycle complete")) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await loop.StopAsync(CancellationToken.None);

        Assert.Contains("Sync cycle processed 4 tenants. Success: 1, Failed: 2", logger.Messages);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Busy"));
        Assert.Contains($"Tenant sync outcome StoreFailed for tenant {tenantA} (NIP: 1111111111). Error: Invoice data could not be stored.", logger.Messages);
        Assert.Contains($"Tenant sync outcome StoreFailed for tenant {tenantB} (NIP: 2222222222). Error: Invoice data could not be stored.", logger.Messages);
    }

    [Fact]
    public async Task SyncLoop_CycleTimesOut_LogsErrorAndKeepsRunning()
    {
        await using var services = Services();
        await MigrateAsync(services);
        var cycleRan = new TaskCompletionSource();
        _sync.SyncAllTenantsAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<TenantSyncResult>>>(_ =>
        {
            cycleRan.TrySetResult();
            throw new TaskCanceledException("timeout", new TimeoutException());
        });
        var logger = new ListLogger<InvoiceSyncService>();
        var loop = SyncLoop(services, logger);

        await loop.StartAsync(CancellationToken.None);
        await cycleRan.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!logger.Levels.Contains(LogLevel.Error) && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.False(loop.ExecuteTask!.IsCompleted);
        await loop.StopAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, m => m.StartsWith("Unhandled error in sync cycle"));
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
            TimeProvider.System,
            logger);

        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await loop.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Levels, l => l >= LogLevel.Error);
    }

    [Fact]
    public async Task SyncLoop_UnknownEnvironment_LogsAndSkipsTheCycle()
    {
        await using var services = Services();
        await MigrateAsync(services);
        var logger = new ListLogger<InvoiceSyncService>();
        var loop = SyncLoop(services, logger, "tset");

        await loop.StartAsync(CancellationToken.None);
        await WaitForErrorAsync(logger);
        await loop.StopAsync(CancellationToken.None);

        await _sync.DidNotReceive().SyncAllTenantsAsync(Arg.Any<CancellationToken>());
        Assert.Contains("Unknown KSeF environment 'tset'", logger.Exceptions.Single());
        Assert.DoesNotContain(logger.Messages, m => m.StartsWith("Sync cycle complete"));
    }

    [Fact]
    public async Task SendLoop_UnknownEnvironment_KeepsPolling_RepeatedErrorIsRateLimited()
    {
        await using var services = Services();
        await MigrateAsync(services);
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment).Returns("tset");
        var refreshes = 0;
        systemConfig.RefreshCacheAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref refreshes);
            return Task.CompletedTask;
        });
        var now = DateTimeOffset.UnixEpoch;
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(_ => now);
        var logger = new ListLogger<IssuedInvoiceSendService>();
        var loop = new IssuedInvoiceSendService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new KSeFRuntimeConfig(systemConfig, new KSeFClientOptions(), Substitute.For<ICryptographyService>(), NullLogger<KSeFRuntimeConfig>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Issuing:PollSeconds"] = "0" }).Build(),
            time,
            logger);

        await loop.StartAsync(CancellationToken.None);
        await WaitForErrorAsync(logger);
        var seen = Volatile.Read(ref refreshes);
        while (Volatile.Read(ref refreshes) < seen + 50)
            await Task.Delay(5);
        Assert.Single(logger.Exceptions);

        now = now.AddMinutes(16);
        await WaitForErrorAsync(logger, count: 2);

        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment).Returns("other");
        await WaitForErrorAsync(logger, count: 3);
        await loop.StopAsync(CancellationToken.None);

        Assert.Equal(3, logger.Exceptions.Count);
        Assert.Contains("Unknown KSeF environment 'tset'", logger.Exceptions[1]);
        Assert.Contains("Unknown KSeF environment 'other'", logger.Exceptions[2]);
    }

    [Fact]
    public async Task SendLoop_ProcessorFails_EscalationStillRunsEveryCycle_ErrorLoggedOncePerWindow()
    {
        var escalations = 0;
        await using var services = Services(s => s
            .AddScoped<IssuedInvoiceSendProcessor>(_ => throw new EncryptionKeyUnavailableException("Encryption key is not configured."))
            .AddScoped(_ =>
            {
                Interlocked.Increment(ref escalations);
                return Escalation();
            }));
        await MigrateAsync(services);
        var now = DateTimeOffset.UnixEpoch;
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(_ => now);
        var logger = new ListLogger<IssuedInvoiceSendService>();
        var loop = new IssuedInvoiceSendService(
            services.GetRequiredService<IServiceScopeFactory>(),
            RuntimeConfig("test"),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Issuing:PollSeconds"] = "0" }).Build(),
            time,
            logger);

        await loop.StartAsync(CancellationToken.None);
        await WaitForErrorAsync(logger);
        var seen = Volatile.Read(ref escalations);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref escalations) < seen + 20 && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        Assert.True(Volatile.Read(ref escalations) >= seen + 20);
        Assert.Single(logger.Exceptions);

        now = now.AddMinutes(16);
        await WaitForErrorAsync(logger, count: 2);
        await loop.StopAsync(CancellationToken.None);

        Assert.All(logger.Exceptions, e => Assert.Equal("Encryption key is not configured.", e));
    }

    private static async Task WaitForErrorAsync<T>(ListLogger<T> logger, int count = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (logger.Exceptions.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(logger.Exceptions.Count >= count);
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
        private readonly List<string> _exceptions = [];
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_levels)
                    return _messages.ToList();
            }
        }

        public IReadOnlyList<string> Exceptions
        {
            get
            {
                lock (_levels)
                    return _exceptions.ToList();
            }
        }

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
            {
                _levels.Add(logLevel);
                _messages.Add(formatter(state, exception));
                if (exception is not null)
                    _exceptions.Add(exception.Message);
            }
        }
    }
}
