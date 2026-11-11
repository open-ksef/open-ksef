using System.Buffers.Binary;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>The per-tenant advisory lock shared by the API manual sync and the Worker cycle.</summary>
public sealed class SyncLockTests : IAsyncLifetime
{
    private const string NipA = "1111111111";
    private const string NipB = "2222222222";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly DateTime LastSync = DateTime.UtcNow.AddHours(-4);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private Func<KSeFQueryCriteria, Task<KSeFInvoiceQueryResult>> _received = c => Task.FromResult(Page([]));
    private int _receivedQueries;
    private int _receivedQueriesA;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                if (criteria.Direction != InvoiceDirection.Received)
                    return Task.FromResult(Page([], criteria.DateTo));
                Interlocked.Increment(ref _receivedQueries);
                if (criteria.SubjectNip == NipA)
                    Interlocked.Increment(ref _receivedQueriesA);
                return _received(criteria);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Xml());

        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        foreach (var (id, nip) in new[] { (_tenantA, NipA), (_tenantB, NipB) })
        {
            db.Tenants.Add(new Tenant
            {
                Id = id,
                UserId = "user-1",
                Nip = nip,
                CreatedAt = now,
                UpdatedAt = now,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = id, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
            });
            db.SyncStates.Add(new SyncState { Id = Guid.NewGuid(), TenantId = id, LastSyncedAt = LastSync, LastSuccessfulSync = LastSync });
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db, TimeSpan? manualLockWait = null) => new(
        db,
        _gateway,
        new InvoiceService(db, _notifications, NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero, ManualLockWait = manualLockWait ?? TimeSpan.FromMinutes(1) }),
        new XmlDownloadBlocks(),
        TimeProvider.System,
        NullLogger<TenantSyncService>.Instance);

    private async Task<IReadOnlyList<TenantSyncResult>> WorkerCycleAsync()
    {
        await using var db = NewContext();
        return await SyncService(db).SyncAllTenantsAsync();
    }

    private async Task<TenantSyncResult> ManualSyncAsync(TimeSpan? manualLockWait = null, CancellationToken cancellationToken = default)
    {
        await using var db = NewContext();
        return await SyncService(db, manualLockWait).SyncTenantAsync(_tenantA, "user-1", cancellationToken: cancellationToken);
    }

    // A pooled connection keeps its session, and so its advisory lock, until reused.
    private string UnpooledConnectionString => _postgres.GetConnectionString() + ";Pooling=false";

    private async Task<NpgsqlConnection> HoldLockAsync(Guid tenantId)
    {
        var connection = new NpgsqlConnection(UnpooledConnectionString);
        await connection.OpenAsync();
        Assert.True(await TryLockAsync(connection, tenantId));
        return connection;
    }

    private async Task<bool> LockIsFreeAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(UnpooledConnectionString);
        await connection.OpenAsync();
        return await TryLockAsync(connection, tenantId);
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, Guid tenantId)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(1263748422, @key)", connection);
        command.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt32LittleEndian(tenantId.ToByteArray()));
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> NumbersAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await db.InvoiceHeaders.Where(i => i.TenantId == tenantId).OrderBy(i => i.KSeFInvoiceNumber).Select(i => i.KSeFInvoiceNumber).ToListAsync();
    }

    private async Task<SyncState> StateAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await db.SyncStates.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
    }

    private int Notifications(Guid tenantId) => _notifications.ReceivedCalls()
        .Count(c => c.GetMethodInfo().Name == nameof(INotificationService.NotifyNewInvoiceAsync) && ((NewInvoiceDetectedEvent)c.GetArguments()[0]!).TenantId == tenantId);

    private static KSeFInvoiceQueryResult Page(IReadOnlyList<KSeFInvoiceHeader> invoices, DateTime? continuationAt = null) => new(invoices, continuationAt);

    private static KSeFInvoiceHeader Bought(string number, string nip) => new(
        number, "FV 1/2026", "7740001454", "Dostawca", nip, "Moja firma", 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");

    private static byte[] Xml() => Encoding.UTF8.GetBytes(
        $"""<Faktura xmlns="{Fa3}"><Fa><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

    [Fact]
    public async Task Worker_SkipsALockedTenant_AndSyncsTheOthers()
    {
        _received = c => Task.FromResult(Page([Bought($"NEW-{c.SubjectNip}", c.SubjectNip)]));
        var stateBefore = await StateAsync(_tenantA);

        await using (await HoldLockAsync(_tenantA))
        {
            var results = await WorkerCycleAsync();

            var locked = results.Single(r => r.TenantId == _tenantA);
            Assert.Equal(TenantSyncOutcome.Busy, locked.Outcome);
            Assert.Equal(TenantSyncOutcome.Success, results.Single(r => r.TenantId == _tenantB).Outcome);
        }

        Assert.Empty(await NumbersAsync(_tenantA));
        Assert.Equal(0, Notifications(_tenantA));
        var stateAfter = await StateAsync(_tenantA);
        Assert.Equal(stateBefore.LastSuccessfulSync, stateAfter.LastSuccessfulSync);
        Assert.Equal(stateBefore.LastSyncedAt, stateAfter.LastSyncedAt);

        Assert.Equal([$"NEW-{NipB}"], await NumbersAsync(_tenantB));
        Assert.Equal(1, Notifications(_tenantB));
        Assert.True((await StateAsync(_tenantB)).LastSuccessfulSync > LastSync);
        await using var db = NewContext();
        Assert.NotNull((await db.InvoiceHeaders.SingleAsync(i => i.TenantId == _tenantB)).XmlFetchedAt);
        Assert.True(await LockIsFreeAsync(_tenantB));
    }

    [Fact]
    public async Task ManualSync_WaitsForTheLock_ThenRuns()
    {
        _received = c => Task.FromResult(Page([Bought("NEW-1", NipA)]));
        var holder = await HoldLockAsync(_tenantA);

        var manual = ManualSyncAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(manual.IsCompleted);
        Assert.Equal(0, _receivedQueries);
        await holder.DisposeAsync();

        Assert.Equal(TenantSyncOutcome.Success, (await manual).Outcome);
        Assert.Equal(["NEW-1"], await NumbersAsync(_tenantA));
        Assert.True(await LockIsFreeAsync(_tenantA));
    }

    [Fact]
    public async Task ManualSync_ReturnsBusy_AndChangesNothing_WhenTheWaitRunsOut()
    {
        _received = c => Task.FromResult(Page([Bought("NEW-1", NipA)]));
        var stateBefore = await StateAsync(_tenantA);

        await using (await HoldLockAsync(_tenantA))
        {
            var result = await ManualSyncAsync(TimeSpan.Zero);
            Assert.Equal(TenantSyncOutcome.Busy, result.Outcome);
            Assert.Equal(TenantSyncService.BusyMessage, result.ErrorMessage);
        }

        Assert.Equal(0, _receivedQueries);
        Assert.Empty(await NumbersAsync(_tenantA));
        Assert.Equal(stateBefore.LastSuccessfulSync, (await StateAsync(_tenantA)).LastSuccessfulSync);
    }

    [Fact]
    public async Task WorkerAndManualSync_Concurrently_StoreEachInvoiceOnce_NotifyOnce_NoStoreFailed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maxInFlight = 0;
        _received = async c =>
        {
            if (c.SubjectNip != NipA)
                return Page([]);
            lock (gate)
                maxInFlight = Math.Max(maxInFlight, ++inFlight);
            await gate.Task;
            lock (gate)
                inFlight--;
            return Page([Bought("RACE-1", NipA), Bought("RACE-2", NipA)]);
        };

        var worker = WorkerCycleAsync();
        var manual = ManualSyncAsync();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (Volatile.Read(ref _receivedQueriesA) < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        gate.SetResult();

        var workerResults = await worker;
        var manualResult = await manual;

        Assert.Equal(1, maxInFlight);
        Assert.DoesNotContain(workerResults, r => r.Outcome == TenantSyncOutcome.StoreFailed);
        Assert.Equal(TenantSyncOutcome.Success, manualResult.Outcome);
        Assert.Equal(["RACE-1", "RACE-2"], await NumbersAsync(_tenantA));
        Assert.Equal(2, Notifications(_tenantA));
        Assert.True(await LockIsFreeAsync(_tenantA));
    }

    [Fact]
    public async Task Lock_LivesOnItsOwnSession_ThatEndsWithTheRun_WithoutAnyUnlock()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _received = async _ =>
        {
            await gate.Task;
            return Page([]);
        };

        var manual = ManualSyncAsync();
        while (Volatile.Read(ref _receivedQueries) < 1)
            await Task.Delay(20);
        var holder = await LockHolderPidAsync(_tenantA);
        Assert.NotNull(holder);
        gate.SetResult();
        Assert.Equal(TenantSyncOutcome.Success, (await manual).Outcome);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await BackendAliveAsync(holder.Value) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.False(await BackendAliveAsync(holder.Value));
        Assert.Null(await LockHolderPidAsync(_tenantA));
        Assert.True(await LockIsFreeAsync(_tenantA));
    }

    private async Task<int?> LockHolderPidAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(UnpooledConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND classid = 1263748422 AND objid = @key::oid AND granted", connection);
        command.Parameters.AddWithValue("key", (long)(uint)BinaryPrimitives.ReadInt32LittleEndian(tenantId.ToByteArray()));
        return await command.ExecuteScalarAsync() as int?;
    }

    private async Task<bool> BackendAliveAsync(int pid)
    {
        await using var connection = new NpgsqlConnection(UnpooledConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE pid = @pid", connection);
        command.Parameters.AddWithValue("pid", pid);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    [Fact]
    public async Task Lock_IsReleased_AfterAGatewayFailure_AndAfterCancellation()
    {
        _received = _ => throw new HttpRequestException("KSeF down");
        Assert.Equal(TenantSyncOutcome.Failed, (await ManualSyncAsync()).Outcome);
        Assert.True(await LockIsFreeAsync(_tenantA));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        _received = async _ =>
        {
            await gate.Task.WaitAsync(stop.Token);
            return Page([]);
        };
        var cancelled = ManualSyncAsync(cancellationToken: stop.Token);
        while (Volatile.Read(ref _receivedQueries) < 2)
            await Task.Delay(20);
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.True(await LockIsFreeAsync(_tenantA));
    }
}
