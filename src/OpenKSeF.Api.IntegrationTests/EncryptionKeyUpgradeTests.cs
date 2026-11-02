using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class EncryptionKeyUpgradeTests : IAsyncLifetime
{
    private const string Token = "ksef-token-plaintext";
    private const string Certificate = "certificate-plaintext";
    private const string MalformedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly byte[] _oldKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _newKey = RandomNumberGenerator.GetBytes(32);
    private readonly ListLogger _logs = new();
    private Guid _tokenCredentialId;
    private Guid _certificateCredentialId;

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private string OldKey => Convert.ToBase64String(_oldKey);

    private string NewKey => Convert.ToBase64String(_newKey);

    private static IConfiguration Config(string? key, string? newKey = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY"] = key, ["ENCRYPTION_KEY_NEW"] = newKey })
        .Build();

    private async Task SeedAsync(byte[] credentialKey, bool withRow)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var encryption = new AesGcmEncryptionService(credentialKey);
        var tenantId = Guid.NewGuid();
        _tokenCredentialId = Guid.NewGuid();
        _certificateCredentialId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1234563218", CreatedAt = Now, UpdatedAt = Now });
        db.KSeFCredentials.AddRange(
            new KSeFCredential { Id = _tokenCredentialId, TenantId = tenantId, Type = CredentialType.Token, EncryptedToken = encryption.Encrypt(Token), CreatedAt = Now, UpdatedAt = Now },
            new KSeFCredential { Id = _certificateCredentialId, TenantId = tenantId, Type = CredentialType.Certificate, EncryptedCertificateData = encryption.Encrypt(Certificate), CreatedAt = Now, UpdatedAt = Now });
        db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.IsInitialized, Value = "true", UpdatedAt = Now });
        if (withRow)
            db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.EncryptionKey, Value = OldKey, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    private async Task AdoptAsync(string? configuredKey)
    {
        await using var db = NewContext();
        await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(configuredKey), _logs);
    }

    private async Task<bool> RowExistsAsync()
    {
        await using var db = NewContext();
        return await db.SystemConfigs.AnyAsync(c => c.Key == SystemConfigKeys.EncryptionKey);
    }

    private async Task<(string Token, string Certificate)> CiphertextAsync()
    {
        await using var db = NewContext();
        var token = await db.KSeFCredentials.SingleAsync(c => c.Id == _tokenCredentialId);
        var certificate = await db.KSeFCredentials.SingleAsync(c => c.Id == _certificateCredentialId);
        return (token.EncryptedToken!, certificate.EncryptedCertificateData!);
    }

    private async Task AssertAllDecryptAsync(byte[] key)
    {
        var (token, certificate) = await CiphertextAsync();
        var encryption = new AesGcmEncryptionService(key);
        Assert.Equal(Token, encryption.Decrypt(token));
        Assert.Equal(Certificate, encryption.Decrypt(certificate));
    }

    private async Task<IEncryptionService> ResolveAsHostAsync(string? configuredKey)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        var configuration = Config(configuredKey);
        services.AddSingleton(configuration);
        services.AddSingleton<ISystemConfigService, SystemConfigService>();
        services.AddEncryptionService(configuration);
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ISystemConfigService>().RefreshCacheAsync();
        return provider.GetRequiredService<IEncryptionService>();
    }

    private void AssertNoSecretsLogged()
    {
        foreach (var secret in new[] { OldKey, NewKey, MalformedKey, Token, Certificate })
            Assert.DoesNotContain(_logs.Entries, e => e.Text.Contains(secret));
    }

    [Fact]
    public async Task ValidEnv_MovesKeyOut_AndReEncryptsAll()
    {
        await SeedAsync(_oldKey, withRow: true);

        await AdoptAsync(NewKey);

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_newKey);
        var (token, certificate) = await CiphertextAsync();
        var old = new AesGcmEncryptionService(_oldKey);
        Assert.ThrowsAny<CryptographicException>(() => old.Decrypt(token));
        Assert.ThrowsAny<CryptographicException>(() => old.Decrypt(certificate));
        await using var db = NewContext();
        Assert.Equal("true", (await db.SystemConfigs.FindAsync(SystemConfigKeys.IsInitialized))!.Value);
        Assert.Equal(Now, (await db.KSeFCredentials.SingleAsync(c => c.Id == _tokenCredentialId)).UpdatedAt);
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task EnvEqualToRow_MovesKeyOut()
    {
        await SeedAsync(_oldKey, withRow: true);

        await AdoptAsync(OldKey);

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_oldKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(MalformedKey)]
    [InlineData("generate_with_openssl_rand")]
    public async Task NoOrMalformedEnv_StaysInLegacyMode_AndLogsError(string? configuredKey)
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();

        await AdoptAsync(configuredKey);

        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error);
        var encryption = await ResolveAsHostAsync(configuredKey);
        Assert.Equal(Token, encryption.Decrypt(before.Token));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task OneCorruptCiphertext_ChangesNothing_AndNamesTheCredential()
    {
        await SeedAsync(_oldKey, withRow: true);
        var corruptId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var tenantId = (await db.KSeFCredentials.FirstAsync()).TenantId;
            db.KSeFCredentials.Add(new KSeFCredential { Id = corruptId, TenantId = tenantId, EncryptedToken = Convert.ToBase64String(new byte[64]), CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }
        var before = await CiphertextAsync();

        await AdoptAsync(NewKey);

        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        var critical = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(corruptId.ToString(), critical.Text);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task TwoHostsAdoptConcurrently_OneWins_AndAllDecrypt()
    {
        await SeedAsync(_oldKey, withRow: true);

        await Task.WhenAll(AdoptAsync(NewKey), AdoptAsync(NewKey));

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_newKey);
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task WorkerAheadOfMigrations_NoCredentialsTable_NoCritical()
    {
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "SystemConfigs" (
                    "Key" character varying(128) NOT NULL PRIMARY KEY,
                    "Value" text NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL)
                """);
        }

        await AdoptAsync(NewKey);

        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task AfterAdoption_EnvOnlyResolutionDecrypts()
    {
        await SeedAsync(_oldKey, withRow: true);
        await AdoptAsync(NewKey);

        var encryption = await ResolveAsHostAsync(NewKey);

        var (token, _) = await CiphertextAsync();
        Assert.Equal(Token, encryption.Decrypt(token));
    }

    [Fact]
    public async Task RowAbsent_RightKey_NoCritical()
    {
        await SeedAsync(_newKey, withRow: false);

        await AdoptAsync(NewKey);

        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task RowAbsent_WrongKey_LogsCriticalNamingCredentials_NothingChanges()
    {
        await SeedAsync(_oldKey, withRow: false);
        var before = await CiphertextAsync();

        await AdoptAsync(NewKey);

        var critical = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(_tokenCredentialId.ToString(), critical.Text);
        Assert.Equal(before, await CiphertextAsync());
        AssertNoSecretsLogged();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MalformedKey)]
    public async Task RowAbsent_NoOrMalformedKey_LogsCritical_NoThrow(string? configuredKey)
    {
        await SeedAsync(_oldKey, withRow: false);

        await AdoptAsync(configuredKey);

        Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Rotate_ReEncryptsAll_AndReRunIsIdempotent()
    {
        await SeedAsync(_oldKey, withRow: false);

        await using (var db = NewContext())
            Assert.Equal(2, await EncryptionKeyMigration.RotateAsync(db, _oldKey, _newKey));
        await AssertAllDecryptAsync(_newKey);
        var afterFirst = await CiphertextAsync();

        await using (var db = NewContext())
            Assert.Equal(0, await EncryptionKeyMigration.RotateAsync(db, _oldKey, _newKey));
        Assert.Equal(afterFirst, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_PrintsCountsOnly_AndExitsZero()
    {
        await SeedAsync(_oldKey, withRow: false);
        var output = new StringWriter();

        await using var db = NewContext();
        var exitCode = await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), output);

        Assert.Equal(0, exitCode);
        Assert.Equal("Re-encrypted 2 credentials.", output.ToString().Trim());
        await AssertAllDecryptAsync(_newKey);
    }

    [Fact]
    public async Task RotateCommand_RowPresent_Refuses()
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();

        await using var db = NewContext();
        var exitCode = await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(NewKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_BadKeys_Refuse_WithoutPrintingThem()
    {
        await SeedAsync(_oldKey, withRow: false);
        var before = await CiphertextAsync();
        var cases = new[] { Config(OldKey), Config(OldKey, MalformedKey), Config(OldKey, OldKey), Config(null, NewKey), Config(MalformedKey, NewKey) };

        foreach (var configuration in cases)
        {
            var output = new StringWriter();
            await using var db = NewContext();
            Assert.Equal(1, await EncryptionKeyMigration.RunRotateCommandAsync(db, configuration, output));
            Assert.DoesNotContain(OldKey, output.ToString());
            Assert.DoesNotContain(NewKey, output.ToString());
            Assert.DoesNotContain(MalformedKey, output.ToString());
        }

        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_CorruptCredential_ExitsOne_NothingWritten()
    {
        await SeedAsync(_oldKey, withRow: false);
        await using (var db = NewContext())
        {
            var tenantId = (await db.KSeFCredentials.FirstAsync()).TenantId;
            db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, EncryptedToken = Convert.ToBase64String(new byte[64]), CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }
        var before = await CiphertextAsync();

        await using var rotateDb = NewContext();
        var exitCode = await EncryptionKeyMigration.RunRotateCommandAsync(rotateDb, Config(OldKey, NewKey), new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Equal(before, await CiphertextAsync());
    }

    private sealed class ListLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Text)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Text)> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception) + exception));
        }
    }
}
