using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class SystemConfigServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SystemConfigService _service;

    public SystemConfigServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Transient);

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ENCRYPTION_KEY"] = "env-encryption-key-base64",
                ["KSeF:Environment"] = "test",
            })
            .Build();

        _service = new SystemConfigService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<SystemConfigService>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private SystemConfigService ServiceWithFailingReads(int failures)
    {
        var real = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => failures-- > 0 ? throw new InvalidOperationException("database down") : real.CreateScope());
        return new SystemConfigService(
            scopes,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KSeF:BaseUrl"] = "test" }).Build(),
            NullLogger<SystemConfigService>.Instance);
    }

    [Fact]
    public async Task LoadCache_ReadKeepsFailing_Throws()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceWithFailingReads(int.MaxValue).LoadCacheAsync(retryDelay: TimeSpan.Zero));

        Assert.Equal("database down", error.Message);
    }

    [Fact]
    public async Task LoadCache_TransientFailure_RetriesAndGetsTheRow()
    {
        await _service.SetValueAsync(SystemConfigKeys.KSeFEnvironment, "production");
        var service = ServiceWithFailingReads(4);

        await service.LoadCacheAsync(retryDelay: TimeSpan.Zero);

        Assert.Equal("production", service.GetValue(SystemConfigKeys.KSeFEnvironment));
    }

    [Fact]
    public async Task LoadCache_NoRowsBeforeTheWizard_Succeeds_EnvironmentVariableApplies()
    {
        var service = ServiceWithFailingReads(0);

        await service.LoadCacheAsync(retryDelay: TimeSpan.Zero);

        Assert.False(service.IsInitialized);
        Assert.Equal("test", service.GetValue(SystemConfigKeys.KSeFBaseUrl));
    }

    [Fact]
    public async Task RefreshCache_ReadFails_KeepsRunning()
    {
        await ServiceWithFailingReads(int.MaxValue).RefreshCacheAsync();
    }

    [Fact]
    public void IsInitialized_WhenEmpty_ReturnsFalse()
    {
        Assert.False(_service.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_AfterSetTrue_ReturnsTrue()
    {
        await _service.SetValueAsync(SystemConfigKeys.IsInitialized, "true");

        Assert.True(_service.IsInitialized);
    }

    [Fact]
    public void GetValue_FallsBackToEnvVar()
    {
        var value = _service.GetValue(SystemConfigKeys.KSeFEnvironment);

        Assert.Equal("test", value);
    }

    [Fact]
    public async Task GetValue_DbOverridesEnvVar()
    {
        await _service.SetValueAsync(SystemConfigKeys.KSeFEnvironment, "demo");

        var value = _service.GetValue(SystemConfigKeys.KSeFEnvironment);

        Assert.Equal("demo", value);
    }

    [Fact]
    public void GetValue_EncryptionKey_DoesNotFallBackToEnvVar()
    {
        Assert.Null(_service.GetValue(SystemConfigKeys.EncryptionKey));
    }

    [Fact]
    public async Task SetValuesAsync_PersistsMultipleValues()
    {
        var values = new Dictionary<string, string>
        {
            [SystemConfigKeys.ExternalBaseUrl] = "https://example.com",
            [SystemConfigKeys.KSeFEnvironment] = "test",
        };

        await _service.SetValuesAsync(values);

        Assert.Equal("https://example.com", _service.GetValue(SystemConfigKeys.ExternalBaseUrl));
        Assert.Equal("test", _service.GetValue(SystemConfigKeys.KSeFEnvironment));
    }

    [Fact]
    public async Task SetValuesAsync_UpdatesExistingValue()
    {
        await _service.SetValueAsync(SystemConfigKeys.ExternalBaseUrl, "https://old.com");
        await _service.SetValueAsync(SystemConfigKeys.ExternalBaseUrl, "https://new.com");

        Assert.Equal("https://new.com", _service.GetValue(SystemConfigKeys.ExternalBaseUrl));
    }

    [Fact]
    public async Task RefreshCacheAsync_ReloadsFromDb()
    {
        // Verify DB access works from a separate scope
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SystemConfigs.Add(new SystemConfig { Key = "dbtest", Value = "ok", UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Read from another scope to confirm InMemory persistence
        int count;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            count = await db.SystemConfigs.CountAsync();
        }

        Assert.True(count > 0, $"Expected records in DB but found {count}");

        await _service.RefreshCacheAsync();
        Assert.Equal("ok", _service.GetValue("dbtest"));
    }

    [Fact]
    public async Task RefreshCacheAsync_PicksUpUpdatedValues()
    {
        await _service.SetValueAsync("refresh_test", "value_1");
        Assert.Equal("value_1", _service.GetValue("refresh_test"));

        // Verify the value is in the DB
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entry = await db.SystemConfigs.FindAsync("refresh_test");
            Assert.NotNull(entry);
            entry.Value = "value_2";
            await db.SaveChangesAsync();
        }

        // Original cache still has old value
        Assert.Equal("value_1", _service.GetValue("refresh_test"));

        // Refresh picks up new value
        await _service.RefreshCacheAsync();
        Assert.Equal("value_2", _service.GetValue("refresh_test"));
    }

    [Fact]
    public void GetValue_UnknownKey_ReturnsNull()
    {
        var value = _service.GetValue("nonexistent_key");

        Assert.Null(value);
    }

    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private async Task<SystemConfigService> ServiceWithRowsAsync(string? encryptionKey, params (string Key, string Value)[] rows)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (key, value) in rows)
                db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var service = new SystemConfigService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ENCRYPTION_KEY"] = encryptionKey,
                ["Auth:ServiceAccount:ClientSecret"] = "env-secret",
            }).Build(),
            NullLogger<SystemConfigService>.Instance);
        await service.LoadCacheAsync(retryDelay: TimeSpan.Zero);
        return service;
    }

    private static string Encrypted(string plaintext) =>
        SystemConfigKeys.EncryptedPrefix + new AesGcmEncryptionService(Convert.FromBase64String(Key)).Encrypt(plaintext);

    [Fact]
    public async Task EncryptedSecret_ReadsAsPlaintext_PlainSecretReadsAsIs()
    {
        var service = await ServiceWithRowsAsync(Key,
            (SystemConfigKeys.PushRelayApiKey, Encrypted("relay-key")),
            (SystemConfigKeys.FirebaseCredentialsJson, "{\"type\":\"service_account\"}"));

        Assert.Equal("relay-key", service.GetValue(SystemConfigKeys.PushRelayApiKey));
        Assert.Equal("{\"type\":\"service_account\"}", service.GetValue(SystemConfigKeys.FirebaseCredentialsJson));
    }

    [Fact]
    public async Task KeyRow_IsNeitherExposedNorUsedToDecrypt()
    {
        var service = await ServiceWithRowsAsync(null,
            (SystemConfigKeys.EncryptionKey, Key),
            (SystemConfigKeys.SetupTokenSigningKey, Encrypted("signing-key")));

        Assert.Null(service.GetValue(SystemConfigKeys.EncryptionKey));
        Assert.Null(service.GetValue(SystemConfigKeys.SetupTokenSigningKey));
    }

    [Theory]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=")]
    [InlineData(null)]
    public async Task EncryptedSecret_WrongOrMissingKey_IsIgnored_FallsBackToEnv(string? encryptionKey)
    {
        var service = await ServiceWithRowsAsync(encryptionKey,
            (SystemConfigKeys.ApiClientSecret, Encrypted("db-secret")),
            (SystemConfigKeys.ExternalBaseUrl, "https://example.test"));

        Assert.Equal("env-secret", service.GetValue(SystemConfigKeys.ApiClientSecret));
        Assert.Equal("https://example.test", service.GetValue(SystemConfigKeys.ExternalBaseUrl));
    }

    [Fact]
    public async Task NonSecretWithPrefix_StaysLiteral()
    {
        var service = await ServiceWithRowsAsync(Key, (SystemConfigKeys.ExternalBaseUrl, "enc:not-a-secret"));

        Assert.Equal("enc:not-a-secret", service.GetValue(SystemConfigKeys.ExternalBaseUrl));
    }
}
