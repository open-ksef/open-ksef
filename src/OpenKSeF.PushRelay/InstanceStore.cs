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
    private readonly SemaphoreSlim _writeLock = new(1, 1);

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

    public async Task<(string InstanceId, string ApiKey)> RegisterAsync(string instanceUrl)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("o");

        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO instances (instance_id, api_key, instance_url, registered_at, last_seen_at, enabled)
                VALUES ($id, $key, $url, $now, $now, 1)
                """;
            cmd.Parameters.AddWithValue("$id", instanceId);
            cmd.Parameters.AddWithValue("$key", apiKey);
            cmd.Parameters.AddWithValue("$url", (object?)instanceUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogInformation("Registered instance {InstanceId} from {Url}", instanceId, instanceUrl);
        return (instanceId, apiKey);
    }

    public async Task<InstanceRecord?> GetAsync(string instanceId)
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
        await _writeLock.WaitAsync();
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
            _writeLock.Release();
        }
    }

    public async Task SetEnabledAsync(string instanceId, bool enabled)
    {
        await _writeLock.WaitAsync();
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
            _writeLock.Release();
        }

        _logger.LogInformation("Instance {InstanceId} enabled={Enabled}", instanceId, enabled);
    }

    public async Task<List<InstanceRecord>> ListAsync()
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
        _writeLock.Dispose();
        _db.Dispose();
    }
}
