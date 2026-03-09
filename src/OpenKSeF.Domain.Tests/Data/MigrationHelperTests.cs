using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Domain.Tests.Data;

public class MigrationHelperTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"openksef-migration-helper-{Guid.NewGuid():N}.db");

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
    public async Task ApplyMigrationsIdempotentlyAsync_RepairsMissingHistoryWhenSchemaAlreadyExists()
    {
        await using (var setupContext = CreateContext())
        {
            await setupContext.Database.MigrateAsync();
            await setupContext.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\"");
        }

        await using var context = CreateContext();

        var appliedBefore = await context.Database.GetAppliedMigrationsAsync();
        Assert.Empty(appliedBefore);

        await MigrationHelper.ApplyMigrationsIdempotentlyAsync(context, NullLogger.Instance);

        var appliedAfter = await context.Database.GetAppliedMigrationsAsync();
        var expected = context.Database.GetMigrations();
        Assert.Equal(expected, appliedAfter);
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
            .Options;

        return new ApplicationDbContext(options);
    }
}
