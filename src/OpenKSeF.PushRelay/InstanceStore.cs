using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace OpenKSeF.PushRelay;

public sealed class InstanceRecord
{
    public string InstanceId { get; init; } = null!;
    public string ApiKey { get; init; } = null!;
    public string? InstanceUrl { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Lightweight SQLite-backed registry of push relay instances.
/// Each self-hosted OpenKSeF instance registers once and receives a unique HMAC key.
/// Keys are stored as raw values since the relay needs them to verify HMAC signatures.
/// The relay server itself is a trusted environment behind Cloudflare.
/// </summary>
public sealed class InstanceStore
{
    private readonly string _connectionString;
    private readonly ILogger<InstanceStore> _logger;

    public InstanceStore(IConfiguration config, ILogger<InstanceStore> logger)
    {
        _logger = logger;
        var configDir = config["Relay:DataDir"];
        var dataDir = string.IsNullOrEmpty(configDir)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : configDir;
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "instances.db");

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, DefaultTimeout = 30 }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS instances (
                instance_id TEXT PRIMARY KEY,
                api_key TEXT NOT NULL,
                instance_url TEXT,
                registered_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_instances_enabled ON instances(enabled);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts a new instance; <paramref name="replacesInstanceId"/>, when given, must be enabled and is disabled in the same transaction, else nothing is written and null is returned. Only pass it from a caller that proved it holds that instance's key.</summary>
    public async Task<(string InstanceId, string ApiKey)?> RegisterAsync(string instanceUrl, string? replacesInstanceId = null)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("o");

        await using (var db = Open())
        {
            await using var tx = (SqliteTransaction)await db.BeginTransactionAsync();

            if (replacesInstanceId is not null)
            {
                await using var disable = db.CreateCommand();
                disable.Transaction = tx;
                disable.CommandText = "UPDATE instances SET enabled = 0 WHERE instance_id = $id AND enabled = 1";
                disable.Parameters.AddWithValue("$id", replacesInstanceId);
                if (await disable.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    _logger.LogWarning("Re-registration rejected: instance {OldInstanceId} is already disabled", replacesInstanceId);
                    return null;
                }
            }

            await using (var insert = db.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO instances (instance_id, api_key, instance_url, registered_at, last_seen_at, enabled)
                    VALUES ($id, $key, $url, $now, $now, 1)
                    """;
                insert.Parameters.AddWithValue("$id", instanceId);
                insert.Parameters.AddWithValue("$key", apiKey);
                insert.Parameters.AddWithValue("$url", (object?)instanceUrl ?? DBNull.Value);
                insert.Parameters.AddWithValue("$now", now);
                await insert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }

        if (replacesInstanceId is null)
            _logger.LogInformation("Registered instance {InstanceId} from {Url}", instanceId, instanceUrl);
        else
            _logger.LogInformation("Registered instance {InstanceId} from {Url}, disabled replaced instance {OldInstanceId}", instanceId, instanceUrl, replacesInstanceId);
        return (instanceId, apiKey);
    }

    public async Task<InstanceRecord?> GetAsync(string instanceId)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT instance_id, api_key, instance_url, registered_at, last_seen_at, enabled FROM instances WHERE instance_id = $id";
        cmd.Parameters.AddWithValue("$id", instanceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new InstanceRecord
        {
            InstanceId = reader.GetString(0),
            ApiKey = reader.GetString(1),
            InstanceUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
            RegisteredAt = DateTimeOffset.Parse(reader.GetString(3)),
            LastSeenAt = DateTimeOffset.Parse(reader.GetString(4)),
            Enabled = reader.GetInt32(5) != 0,
        };
    }

    public async Task UpdateLastSeenAsync(string instanceId)
    {
        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE instances SET last_seen_at = $now WHERE instance_id = $id";
        cmd.Parameters.AddWithValue("$id", instanceId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetEnabledAsync(string instanceId, bool enabled)
    {
        await using (var db = Open())
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "UPDATE instances SET enabled = $enabled WHERE instance_id = $id";
            cmd.Parameters.AddWithValue("$id", instanceId);
            cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Instance {InstanceId} enabled={Enabled}", instanceId, enabled);
    }

    public async Task<List<InstanceRecord>> ListAsync()
    {
        var results = new List<InstanceRecord>();

        await using var db = Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT instance_id, api_key, instance_url, registered_at, last_seen_at, enabled FROM instances ORDER BY registered_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new InstanceRecord
            {
                InstanceId = reader.GetString(0),
                ApiKey = reader.GetString(1),
                InstanceUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                RegisteredAt = DateTimeOffset.Parse(reader.GetString(3)),
                LastSeenAt = DateTimeOffset.Parse(reader.GetString(4)),
                Enabled = reader.GetInt32(5) != 0,
            });
        }

        return results;
    }
}
