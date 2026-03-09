using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace OpenKSeF.Domain.Data;

public static class MigrationHelper
{
    private const long MigrationAdvisoryLockKey = 884102034792552831;
    private const string HistoryTableName = "__EFMigrationsHistory";
    private const string BaselineTableName = "Tenants";

    public static async Task ApplyMigrationsIdempotentlyAsync(
        ApplicationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var isPostgres = db.Database.IsNpgsql();
        if (isPostgres)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_lock({MigrationAdvisoryLockKey})",
                cancellationToken);
        }

        try
        {
            var allMigrations = db.Database.GetMigrations().ToArray();
            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            var schemaExistsButHistoryMissing =
                allMigrations.Length > 0 &&
                pendingMigrations.Length > 0 &&
                appliedMigrations.Length == 0 &&
                await TableExistsAsync(db, BaselineTableName, cancellationToken);

            if (schemaExistsButHistoryMissing)
            {
                logger.LogWarning(
                    "Detected existing schema with empty migration history. Rebuilding migration history table.");
                await SeedMigrationHistoryAsync(db, allMigrations, cancellationToken);
                pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            }

            if (pendingMigrations.Length > 0)
            {
                logger.LogInformation("Applying {Count} pending migrations", pendingMigrations.Length);
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
        finally
        {
            if (isPostgres)
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey})",
                    cancellationToken);
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task SeedMigrationHistoryAsync(
        ApplicationDbContext db,
        IEnumerable<string> migrationIds,
        CancellationToken cancellationToken)
    {
        var historyRepository = db.GetService<IHistoryRepository>();
        var createHistorySql = historyRepository.GetCreateIfNotExistsScript();
        await db.Database.ExecuteSqlRawAsync(createHistorySql, cancellationToken);

        var productVersion = GetProductVersion();
        foreach (var migrationId in migrationIds)
        {
            await InsertHistoryRowIfMissingAsync(db, migrationId, productVersion, cancellationToken);
        }
    }

    private static string GetProductVersion()
        => typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.0";

    private static async Task<bool> TableExistsAsync(
        ApplicationDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            if (db.Database.IsNpgsql())
            {
                command.CommandText =
                    """
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = @tableName
                    LIMIT 1
                    """;
                AddParameter(command, "@tableName", tableName);
            }
            else if (db.Database.IsSqlite())
            {
                command.CommandText =
                    """
                    SELECT 1
                    FROM sqlite_master
                    WHERE type = 'table' AND name = @tableName
                    LIMIT 1
                    """;
                AddParameter(command, "@tableName", tableName);
            }
            else
            {
                command.CommandText = $"SELECT 1 FROM {tableName} LIMIT 1";
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null && result != DBNull.Value;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task InsertHistoryRowIfMissingAsync(
        ApplicationDbContext db,
        string migrationId,
        string productVersion,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            if (db.Database.IsNpgsql())
            {
                command.CommandText =
                    $"""
                    INSERT INTO "{HistoryTableName}" ("MigrationId", "ProductVersion")
                    VALUES (@migrationId, @productVersion)
                    ON CONFLICT ("MigrationId") DO NOTHING
                    """;
            }
            else if (db.Database.IsSqlite())
            {
                command.CommandText =
                    $"""
                    INSERT OR IGNORE INTO "{HistoryTableName}" ("MigrationId", "ProductVersion")
                    VALUES (@migrationId, @productVersion)
                    """;
            }
            else
            {
                command.CommandText =
                    $"""
                    INSERT INTO "{HistoryTableName}" ("MigrationId", "ProductVersion")
                    VALUES (@migrationId, @productVersion)
                    """;
            }

            AddParameter(command, "@migrationId", migrationId);
            AddParameter(command, "@productVersion", productVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
