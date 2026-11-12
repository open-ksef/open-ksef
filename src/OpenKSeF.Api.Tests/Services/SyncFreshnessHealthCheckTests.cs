using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Tests.Services;

public class SyncFreshnessHealthCheckTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public async Task FreshInstall_WithNoTenants_IsHealthy()
    {
        Assert.Equal(HealthStatus.Healthy, await CheckAsync());
    }

    [Fact]
    public async Task WorkerStoppedBeyondThreeIntervals_IsDegraded()
    {
        AddTenant(credentialAge: TimeSpan.FromDays(30), lastSyncAge: TimeSpan.FromHours(13));

        Assert.Equal(HealthStatus.Degraded, await CheckAsync());
    }

    [Fact]
    public async Task SyncedWithinThreeIntervals_IsHealthy()
    {
        AddTenant(credentialAge: TimeSpan.FromDays(30), lastSyncAge: TimeSpan.FromHours(11));

        Assert.Equal(HealthStatus.Healthy, await CheckAsync());
    }

    [Fact]
    public async Task TenantWithoutCredential_IsIgnored()
    {
        AddTenant(credentialAge: null, lastSyncAge: TimeSpan.FromDays(30));

        Assert.Equal(HealthStatus.Healthy, await CheckAsync());
    }

    [Theory]
    [InlineData(1, HealthStatus.Healthy)]
    [InlineData(13, HealthStatus.Degraded)]
    public async Task NeverSynced_UsesCredentialAge(int credentialAgeHours, HealthStatus expected)
    {
        AddTenant(credentialAge: TimeSpan.FromHours(credentialAgeHours), lastSyncAge: null, withSyncState: false);

        Assert.Equal(expected, await CheckAsync());
    }

    [Fact]
    public async Task SyncStateWithoutRun_UsesCredentialAge()
    {
        AddTenant(credentialAge: TimeSpan.FromHours(13), lastSyncAge: null);

        Assert.Equal(HealthStatus.Degraded, await CheckAsync());
    }

    [Fact]
    public async Task Degraded_DescriptionHasNoNip()
    {
        AddTenant(credentialAge: TimeSpan.FromDays(30), lastSyncAge: TimeSpan.FromDays(2));

        var result = await Check().CheckHealthAsync(new HealthCheckContext());

        Assert.Equal("1 tenant(s) not synced for over 12h", result.Description);
    }

    [Fact]
    public async Task LogsOnlyWhenTheStatusChanges()
    {
        var logger = Substitute.For<ILogger<SyncFreshnessHealthCheck>>();
        var check = Check(logger);

        await check.CheckHealthAsync(new HealthCheckContext());
        AddTenant(credentialAge: TimeSpan.FromDays(30), lastSyncAge: TimeSpan.FromDays(2));
        for (var i = 0; i < 3; i++)
            await check.CheckHealthAsync(new HealthCheckContext());
        _db.SyncStates.Single().LastSyncedAt = _now;
        _db.SaveChanges();
        await check.CheckHealthAsync(new HealthCheckContext());
        await check.CheckHealthAsync(new HealthCheckContext());

        var levels = logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => (LogLevel)c.GetArguments()[0]!);
        Assert.Equal([LogLevel.Warning, LogLevel.Information], levels);
    }

    private SyncFreshnessHealthCheck Check(ILogger<SyncFreshnessHealthCheck>? logger = null) =>
        new(new ServiceCollection().AddSingleton(_db).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenantSyncOptions { IntervalHours = 4 }),
            TimeProvider.System,
            logger ?? NullLogger<SyncFreshnessHealthCheck>.Instance);

    private async Task<HealthStatus> CheckAsync() => (await Check().CheckHealthAsync(new HealthCheckContext())).Status;

    private void AddTenant(TimeSpan? credentialAge, TimeSpan? lastSyncAge, bool withSyncState = true)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), UserId = "user-1", Nip = "1111111111", CreatedAt = _now.AddDays(-60), UpdatedAt = _now };
        _db.Tenants.Add(tenant);
        if (credentialAge is { } age)
            _db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenant.Id, CreatedAt = _now - age, UpdatedAt = _now });
        if (withSyncState)
            _db.SyncStates.Add(new SyncState { Id = Guid.NewGuid(), TenantId = tenant.Id, LastSyncedAt = _now - lastSyncAge });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();
}
