using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Tests.Controllers;

public class CredentialsControllerSyncTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantSyncService _sync = Substitute.For<ITenantSyncService>();
    private readonly string _userId = "user-1";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ManualSyncJobs _jobs;

    public CredentialsControllerSyncTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);
        _jobs = Jobs(_sync, TimeSpan.FromMilliseconds(300));

        var now = DateTime.UtcNow;
        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId, UserId = _userId, Nip = "1111111111", CreatedAt = now, UpdatedAt = now,
            KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
        });
        _db.SaveChanges();
    }

    internal static ManualSyncJobs Jobs(ITenantSyncService sync, TimeSpan? hold = null) => new(
        new ServiceCollection().AddSingleton(sync).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        Substitute.For<IHostApplicationLifetime>(),
        Options.Create(new TenantSyncOptions { ManualSyncHold = hold ?? TimeSpan.FromSeconds(25) }),
        TimeProvider.System,
        NullLogger<ManualSyncJobs>.Instance);

    private CredentialsController CreateController(bool preferAsync = false)
    {
        var context = new DefaultHttpContext();
        if (preferAsync)
            context.Request.Headers["Prefer"] = "respond-async";
        return new CredentialsController(_db, _currentUser, Substitute.For<IEncryptionService>(), _jobs, Substitute.For<ILogger<CredentialsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private void SyncReturns(TenantSyncResult result, bool fullResync = false) =>
        _sync.SyncTenantAsync(_tenantId, _userId, fullResync, Arg.Any<CancellationToken>(), Arg.Any<Action?>())
            .Returns(call =>
            {
                call.Arg<Action?>()?.Invoke();
                return result;
            });

    private TaskCompletionSource<TenantSyncResult> SyncWaits(bool invokeLockAcquired = true)
    {
        var gate = new TaskCompletionSource<TenantSyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sync.SyncTenantAsync(_tenantId, _userId, false, Arg.Any<CancellationToken>(), Arg.Any<Action?>())
            .Returns(call =>
            {
                if (invokeLockAcquired)
                    call.Arg<Action?>()?.Invoke();
                return gate.Task;
            });
        return gate;
    }

    private async Task<TenantSyncStatusResponse> StatusAsync() =>
        Assert.IsType<TenantSyncStatusResponse>(Assert.IsType<OkObjectResult>(await CreateController().SyncStatus(_tenantId)).Value);

    private TenantSyncResult Outcome(TenantSyncOutcome outcome, string? error = null) => new(_tenantId, "1111111111", outcome, ErrorMessage: error);

    [Fact]
    public async Task SyncNow_ReturnsNotFound_WhenTenantDisappearsDuringRun()
    {
        SyncReturns(Outcome(TenantSyncOutcome.TenantNotFound, "Tenant not found."));

        Assert.IsType<NotFoundResult>(await CreateController().SyncNow(_tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task SyncNow_ReturnsConflict_WithoutStartingAJob_WhenMissingCredential()
    {
        _db.KSeFCredentials.RemoveRange(_db.KSeFCredentials);
        await _db.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(await CreateController(preferAsync: true).SyncNow(_tenantId, CancellationToken.None));

        Assert.Equal($$"""{"error":"{{TenantSyncService.MissingCredentialMessage}}"}""", JsonSerializer.Serialize(conflict.Value));
        Assert.Empty(_sync.ReceivedCalls());
    }

    [Fact]
    public async Task SyncNow_ReturnsForbidden_WithoutStartingAJob_ForNonOwner()
    {
        _currentUser.UserId.Returns("someone-else");

        Assert.IsType<ForbidResult>(await CreateController().SyncNow(_tenantId, CancellationToken.None));
        Assert.Empty(_sync.ReceivedCalls());
        Assert.Null(_jobs.Find(_tenantId));
    }

    [Theory]
    [InlineData(TenantSyncOutcome.Failed, 502, "KSeF unavailable.")]
    [InlineData(TenantSyncOutcome.StoreFailed, 500, "Invoice data could not be stored.")]
    [InlineData(TenantSyncOutcome.Busy, 503, TenantSyncService.BusyMessage)]
    public async Task SyncNow_KeepsTodaysStatusAndBody_PerFailedOutcome(TenantSyncOutcome outcome, int status, string error)
    {
        SyncReturns(Outcome(outcome, error));

        var result = Assert.IsType<ObjectResult>(await CreateController().SyncNow(_tenantId, CancellationToken.None));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal($$"""{"error":"{{error}}"}""", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task SyncNow_WithoutPrefer_FastJob_ReturnsTodaysBody()
    {
        var syncedAt = DateTime.UtcNow;
        SyncReturns(new TenantSyncResult(
            TenantId: _tenantId,
            Nip: "1111111111",
            Outcome: TenantSyncOutcome.Success,
            FetchedInvoices: 5,
            NewInvoices: 2,
            SyncedAtUtc: syncedAt,
            QueuedDownloads: 4,
            ErroredDownloads: 1,
            IssuedFetchedInvoices: 3,
            IssuedNewInvoices: 1));

        var ok = Assert.IsType<OkObjectResult>(await CreateController().SyncNow(_tenantId, CancellationToken.None));

        var payload = Assert.IsType<TenantManualSyncResponse>(ok.Value);
        Assert.Equal(new TenantManualSyncResponse(_tenantId, 5, 2, syncedAt, 5, 4, 1, 3, 1), payload);
        var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(5, json.GetProperty("fetchedInvoices").GetInt32());
        Assert.Equal(2, json.GetProperty("newInvoices").GetInt32());
    }

    [Fact]
    public async Task FullResync_PassesTheFlag_AndReturnsTheResult()
    {
        SyncReturns(new TenantSyncResult(_tenantId, "1111111111", TenantSyncOutcome.Success, FetchedInvoices: 7, SyncedAtUtc: DateTime.UtcNow), fullResync: true);

        var ok = Assert.IsType<OkObjectResult>(await CreateController().FullResync(_tenantId, CancellationToken.None));

        Assert.Equal(7, Assert.IsType<TenantManualSyncResponse>(ok.Value).FetchedInvoices);
        Assert.True((await StatusAsync()).FullResync);
    }

    [Fact]
    public async Task SyncNow_WithPrefer_Returns202_WithLocationAndStatus()
    {
        var gate = SyncWaits();

        var accepted = Assert.IsType<AcceptedResult>(await CreateController(preferAsync: true).SyncNow(_tenantId, CancellationToken.None));

        Assert.Equal($"/api/tenants/{_tenantId}/credentials/sync", accepted.Location);
        var status = Assert.IsType<TenantSyncStatusResponse>(accepted.Value);
        Assert.Contains(status.State, new[] { "waiting", "running" });
        Assert.False(status.FullResync);
        Assert.NotNull(status.StartedAtUtc);
        gate.SetResult(Outcome(TenantSyncOutcome.Success));
    }

    [Fact]
    public async Task SyncNow_WithoutPrefer_SlowJob_Returns202WithEmptyBody_AndJobFinishesAfterAbort()
    {
        var gate = SyncWaits();
        using var aborted = new CancellationTokenSource();

        var accepted = Assert.IsType<AcceptedResult>(await CreateController().SyncNow(_tenantId, aborted.Token));
        aborted.Cancel();

        Assert.Null(accepted.Value);
        Assert.True(SpinWait.SpinUntil(() => _jobs.Find(_tenantId)!.Running, TimeSpan.FromSeconds(5)));
        Assert.Equal("running", (await StatusAsync()).State);

        gate.SetResult(new TenantSyncResult(_tenantId, "1111111111", TenantSyncOutcome.Success, FetchedInvoices: 3, NewInvoices: 1, SyncedAtUtc: DateTime.UtcNow));
        await _jobs.Find(_tenantId)!.Completion;

        var status = await StatusAsync();
        Assert.Equal("succeeded", status.State);
        Assert.Equal(3, status.Result!.FetchedInvoices);
        Assert.NotNull(status.FinishedAtUtc);
        await _sync.Received(1).SyncTenantAsync(_tenantId, _userId, false, CancellationToken.None, Arg.Any<Action?>());
    }

    [Fact]
    public async Task SecondPost_WhileRunning_AttachesToTheSameJob()
    {
        var gate = SyncWaits();

        await CreateController(preferAsync: true).SyncNow(_tenantId, CancellationToken.None);
        var first = _jobs.Find(_tenantId);
        await CreateController(preferAsync: true).FullResync(_tenantId, CancellationToken.None);

        Assert.Same(first, _jobs.Find(_tenantId));
        Assert.False((await StatusAsync()).FullResync);
        gate.SetResult(Outcome(TenantSyncOutcome.Success));
        await first!.Completion;
        await _sync.Received(1).SyncTenantAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Action?>());
    }

    [Fact]
    public async Task Status_MapsIdleWaitingRunningFailed_AndReadsLastSuccessfulSync()
    {
        var lastSync = new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);
        _db.SyncStates.Add(new SyncState { Id = Guid.NewGuid(), TenantId = _tenantId, LastSuccessfulSync = lastSync });
        await _db.SaveChangesAsync();

        var idle = await StatusAsync();
        Assert.Equal("idle", idle.State);
        Assert.Equal(lastSync, idle.LastSuccessfulSyncUtc);

        var waiting = SyncWaits(invokeLockAcquired: false);
        await CreateController(preferAsync: true).SyncNow(_tenantId, CancellationToken.None);
        Assert.Equal("waiting", (await StatusAsync()).State);
        waiting.SetResult(Outcome(TenantSyncOutcome.Busy, TenantSyncService.BusyMessage));
        await _jobs.Find(_tenantId)!.Completion;

        var busy = await StatusAsync();
        Assert.Equal("failed", busy.State);
        Assert.Equal(503, busy.ErrorStatus);
        Assert.Equal(TenantSyncService.BusyMessage, busy.Error);
        Assert.Null(busy.Result);

        var running = SyncWaits();
        await CreateController(preferAsync: true).SyncNow(_tenantId, CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => _jobs.Find(_tenantId)!.Running, TimeSpan.FromSeconds(5)));
        Assert.Equal("running", (await StatusAsync()).State);
        running.SetResult(Outcome(TenantSyncOutcome.Failed, "KSeF unavailable."));
        await _jobs.Find(_tenantId)!.Completion;

        var failed = await StatusAsync();
        Assert.Equal("failed", failed.State);
        Assert.Equal(502, failed.ErrorStatus);
        Assert.Equal(lastSync, failed.LastSuccessfulSyncUtc);
    }

    [Fact]
    public async Task Status_ReturnsForbidden_ForNonOwner()
    {
        _currentUser.UserId.Returns("someone-else");

        Assert.IsType<ForbidResult>(await CreateController().SyncStatus(_tenantId));
    }

    [Fact]
    public async Task JobThatThrows_EndsFailed_Not_Faulted()
    {
        _sync.SyncTenantAsync(_tenantId, _userId, false, Arg.Any<CancellationToken>(), Arg.Any<Action?>())
            .Returns<Task<TenantSyncResult>>(_ => throw new InvalidOperationException("boom"));

        var result = Assert.IsType<ObjectResult>(await CreateController().SyncNow(_tenantId, CancellationToken.None));

        Assert.Equal(502, result.StatusCode);
        Assert.Equal("failed", (await StatusAsync()).State);
    }

    [Fact]
    public async Task StopAsync_WaitsForARunningJob()
    {
        var gate = SyncWaits();
        _jobs.Start(_tenantId, _userId, fullResync: false);

        var stop = _jobs.StopAsync(CancellationToken.None);
        await Task.Delay(200);
        Assert.False(stop.IsCompleted);

        gate.SetResult(Outcome(TenantSyncOutcome.Success));
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(true, LogLevel.Information, false)]
    [InlineData(false, LogLevel.Warning, true)]
    public async Task CancelledJob_LogsInformationWithoutStack_OnlyWhenTheApiStops(bool stopping, LogLevel level, bool withException)
    {
        using var stop = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stop.Token);
        var logger = new ListLogger();
        var jobs = new ManualSyncJobs(
            new ServiceCollection().AddSingleton(_sync).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            Options.Create(new TenantSyncOptions()),
            TimeProvider.System,
            logger);
        if (stopping)
            stop.Cancel();
        _sync.SyncTenantAsync(_tenantId, _userId, false, Arg.Any<CancellationToken>(), Arg.Any<Action?>())
            .Returns<Task<TenantSyncResult>>(_ => throw new OperationCanceledException());

        var result = await jobs.Start(_tenantId, _userId, fullResync: false).Completion;

        Assert.Equal(TenantSyncOutcome.Failed, result.Outcome);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.Equal(withException, entry.Exception is not null);
    }

    public void Dispose() => _db.Dispose();

    private sealed class ListLogger : ILogger<ManualSyncJobs>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
