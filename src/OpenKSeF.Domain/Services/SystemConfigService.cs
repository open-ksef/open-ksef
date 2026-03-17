using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Services;

public sealed class SystemConfigService : ISystemConfigService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemConfigService> _logger;
    private ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _cacheLoaded;

    private static readonly Dictionary<string, string> EnvVarMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        [SystemConfigKeys.EncryptionKey] = "ENCRYPTION_KEY",
        [SystemConfigKeys.KSeFEnvironment] = "KSeF:Environment",
        [SystemConfigKeys.KSeFBaseUrl] = "KSeF:BaseUrl",
        [SystemConfigKeys.ExternalBaseUrl] = "APP_EXTERNAL_BASE_URL",
        [SystemConfigKeys.ApiClientSecret] = "Auth:ServiceAccount:ClientSecret",
        [SystemConfigKeys.FirebaseCredentialsJson] = "Firebase:CredentialsJson",
        [SystemConfigKeys.GoogleClientId] = "GOOGLE_CLIENT_ID",
        [SystemConfigKeys.GoogleClientSecret] = "GOOGLE_CLIENT_SECRET",
    };

    public SystemConfigService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SystemConfigService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsInitialized
    {
        get
        {
            var value = GetValue(SystemConfigKeys.IsInitialized);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? GetValue(string key)
    {
        EnsureCacheLoaded();

        if (_cache.TryGetValue(key, out var dbValue) && !string.IsNullOrEmpty(dbValue))
            return dbValue;

        if (EnvVarMapping.TryGetValue(key, out var envKey))
        {
            var envValue = _configuration[envKey];
            if (!string.IsNullOrEmpty(envValue))
                return envValue;
        }

        return null;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await SetValuesAsync(new Dictionary<string, string> { [key] = value }, cancellationToken);
    }

    public async Task SetValuesAsync(IDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        EnsureCacheLoaded();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await EnsureTableExistsAsync(db, cancellationToken);

        var keys = values.Keys.ToArray();
        var existing = await db.SystemConfigs
            .Where(c => keys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var entry))
            {
                entry.Value = value;
                entry.UpdatedAt = now;
            }
            else
            {
                db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, UpdatedAt = now });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (key, value) in values)
            _cache[key] = value;
    }

    public async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await EnsureTableExistsAsync(db, cancellationToken);

            var entries = await db.SystemConfigs.AsNoTracking().ToListAsync(cancellationToken);
            var newCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                newCache[entry.Key] = entry.Value;
            _cache = newCache;
            _cacheLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load system config from database — using env vars only");
            _cacheLoaded = true;
        }
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheLoaded)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            EnsureTableExistsAsync(db, CancellationToken.None).GetAwaiter().GetResult();

            var entries = db.SystemConfigs.AsNoTracking().ToList();
            var newCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                newCache[entry.Key] = entry.Value;
            _cache = newCache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load system config from database — using env vars only");
        }
        finally
        {
            _cacheLoaded = true;
        }
    }

    private volatile bool _tableEnsured;

    private async Task EnsureTableExistsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        if (_tableEnsured)
            return;

        try
        {
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "SystemConfigs" (
                        "Key" character varying(128) NOT NULL,
                        "Value" text NOT NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL,
                        CONSTRAINT "PK_SystemConfigs" PRIMARY KEY ("Key")
                    )
                    """, ct);
            }
            else if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "SystemConfigs" (
                        "Key" TEXT NOT NULL CONSTRAINT "PK_SystemConfigs" PRIMARY KEY,
                        "Value" TEXT NOT NULL,
                        "UpdatedAt" TEXT NOT NULL
                    )
                    """, ct);
            }

            _tableEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure SystemConfigs table exists");
        }
    }
}
