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
public sealed class InstanceStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<InstanceStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public InstanceStore(IConfiguration config, ILogger<InstanceStore> logger)
    {
        _logger = logger;
        var configDir = config["Relay:DataDir"];
        var dataDir = string.IsNullOrEmpty(configDir)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : configDir;
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "instances.db");

        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
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

    /// <summary>Inserts a new instance; <paramref name="replacesInstanceId"/>, when given, is disabled in the same transaction and must only come from a caller that proved it holds that instance's key.</summary>
    public async Task<(string InstanceId, string ApiKey)> RegisterAsync(string instanceUrl, string? replacesInstanceId = null)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("o");

        await _lock.WaitAsync();
        try
        {
            await using var tx = (SqliteTransaction)await _db.BeginTransactionAsync();

            await using (var insert = _db.CreateCommand())
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

            if (replacesInstanceId is not null)
            {
                await using var disable = _db.CreateCommand();
                disable.Transaction = tx;
                disable.CommandText = "UPDATE instances SET enabled = 0 WHERE instance_id = $id";
                disable.Parameters.AddWithValue("$id", replacesInstanceId);
                await disable.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        finally
        {
            _lock.Release();
        }

        if (replacesInstanceId is null)
            _logger.LogInformation("Registered instance {InstanceId} from {Url}", instanceId, instanceUrl);
        else
            _logger.LogInformation("Registered instance {InstanceId} from {Url}, disabled replaced instance {OldInstanceId}", instanceId, instanceUrl, replacesInstanceId);
        return (instanceId, apiKey);
    }

    public async Task<InstanceRecord?> GetAsync(string instanceId)
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadOneAsync(instanceId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<InstanceRecord?> ReadOneAsync(string instanceId)
    {
        await using var cmd = _db.CreateCommand();
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
        await _lock.WaitAsync();
        try
        {
            await using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE instances SET last_seen_at = $now WHERE instance_id = $id";
            cmd.Parameters.AddWithValue("$id", instanceId);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetEnabledAsync(string instanceId, bool enabled)
    {
        await _lock.WaitAsync();
        try
        {
            await using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE instances SET enabled = $enabled WHERE instance_id = $id";
            cmd.Parameters.AddWithValue("$id", instanceId);
            cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }

        _logger.LogInformation("Instance {InstanceId} enabled={Enabled}", instanceId, enabled);
    }

    public async Task<List<InstanceRecord>> ListAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadAllAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<InstanceRecord>> ReadAllAsync()
    {
        var results = new List<InstanceRecord>();

        await using var cmd = _db.CreateCommand();
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

    public void Dispose()
    {
        _lock.Dispose();
        _db.Dispose();
    }
}
