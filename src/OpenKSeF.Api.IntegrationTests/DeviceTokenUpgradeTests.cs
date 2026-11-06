using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class DeviceTokenUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261106101846_AlignSyncColumnsWithKsef";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static DeviceToken Token(string token, string userId, DateTime updatedAt, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Token = token,
        Platform = Platform.Android,
        TenantId = tenantId,
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    [Fact]
    public async Task Migration_KeepsTheNewestRowPerToken_ThenRejectsDuplicates()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        var recent = DateTime.UtcNow.AddDays(-1);
        var switchedOld = Token("switched", "user-a", old);
        var switchedNew = Token("switched", "user-b", recent);
        var repeatOld = Token("repeat", "user-a", old);
        var repeatNew = Token("repeat", "user-a", recent);
        var unique = Token("unique", "user-c", old);

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.DeviceTokens.AddRange(switchedOld, switchedNew, repeatOld, repeatNew, unique);
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            var ids = await db.DeviceTokens.Select(d => d.Id).ToListAsync();
            Assert.Equal(new[] { switchedNew.Id, repeatNew.Id, unique.Id }.Order(), ids.Order());

            db.DeviceTokens.Add(Token("unique", "user-d", DateTime.UtcNow));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task TenantDelete_RemovesItsDevices_KeepsAllCompanyDevices()
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var allCompanies = Token("all-companies", "user-a", now);

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-a", Nip = "1111111111", CreatedAt = now, UpdatedAt = now });
            db.DeviceTokens.AddRange(Token("tenant-bound", "user-a", now, tenantId), allCompanies);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();

            var remaining = await db.DeviceTokens.SingleAsync();
            Assert.Equal(allCompanies.Id, remaining.Id);
            Assert.Null(remaining.TenantId);
        }
    }
}
