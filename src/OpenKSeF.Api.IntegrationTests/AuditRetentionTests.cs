using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class AuditRetentionTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task PurgeUnlinked_DeletesOnlyUnlinkedEventsOlderThan12Months()
    {
        var tenantId = Guid.NewGuid();
        var oldUnlinked = Enumerable.Range(0, 1001).Select(i => Event(null, Now.AddMonths(-12).AddDays(-1 - i))).ToList();
        var recentUnlinked = Event(null, Now.AddMonths(-12).AddDays(1));
        var oldLinked = Event(tenantId, Now.AddYears(-3));
        await using (var db = NewContext())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
            db.AuditEvents.AddRange(oldUnlinked);
            db.AuditEvents.AddRange(recentUnlinked, oldLinked);
            await db.SaveChangesAsync();
        }

        int deleted;
        await using (var db = NewContext())
            deleted = await new AuditRetentionService(db, new FixedTime()).PurgeUnlinkedAsync(CancellationToken.None);

        await using var check = NewContext();
        var remaining = await check.AuditEvents.Select(a => a.Id).ToListAsync();
        Assert.Equal(1001, deleted);
        Assert.Equal(new[] { recentUnlinked.Id, oldLinked.Id }.Order(), remaining.Order());
    }

    private static AuditEvent Event(Guid? tenantId, DateTime occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        OccurredAt = occurredAt,
        ActorUserId = "user-1",
        Action = AuditEvent.TwoFactorReset
    };

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
