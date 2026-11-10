using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Domain.Tests.Data;

public class MigrationHelperTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"openksef-migration-helper-{Guid.NewGuid():N}.db");

    [Fact]
    public void NpgsqlModel_MatchesLatestMigrationSnapshot()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost").Options);

        Assert.False(context.Database.HasPendingModelChanges(), "Model changed without a migration: run dotnet ef migrations add.");
    }

    [Fact]
    public async Task ApplyMigrationsIdempotentlyAsync_AppliesMigrationsOnFreshDatabase()
    {
        await using var context = CreateContext();

        await MigrationHelper.ApplyMigrationsIdempotentlyAsync(context, NullLogger.Instance);

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var expected = context.Database.GetMigrations();
        Assert.Equal(expected, applied);
    }

    [Fact]
    public async Task ApplyMigrationsIdempotentlyAsync_BaselinesOnlyInitialSchema_RunsTheRest()
    {
        await using (var setupContext = CreateContext())
        {
            var initialSchema = setupContext.Database.GetMigrations().First();
            await setupContext.GetService<IMigrator>().MigrateAsync(initialSchema);
            await setupContext.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\"");
        }

        await using var context = CreateContext();

        await MigrationHelper.ApplyMigrationsIdempotentlyAsync(context, NullLogger.Instance);

        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        var notificationEmailColumns = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('Tenants') WHERE name = 'NotificationEmail'")
            .SingleAsync();
        Assert.Equal(1, notificationEmailColumns);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ApplicationDbContext(options);
    }
}
