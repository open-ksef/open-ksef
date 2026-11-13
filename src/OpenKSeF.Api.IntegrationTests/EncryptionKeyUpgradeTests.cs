using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
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

    private ApplicationDbContext NewContext(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).AddInterceptors(interceptors).Options);

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
        var certificateTenantId = Guid.NewGuid();
        _tokenCredentialId = Guid.NewGuid();
        _certificateCredentialId = Guid.NewGuid();
        db.Tenants.AddRange(
            new Tenant { Id = tenantId, UserId = "user-1", Nip = "1234563218", CreatedAt = Now, UpdatedAt = Now },
            new Tenant { Id = certificateTenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
        db.KSeFCredentials.AddRange(
            new KSeFCredential { Id = _tokenCredentialId, TenantId = tenantId, Type = CredentialType.Token, EncryptedToken = encryption.Encrypt(Token), CreatedAt = Now, UpdatedAt = Now },
            new KSeFCredential { Id = _certificateCredentialId, TenantId = certificateTenantId, Type = CredentialType.Certificate, EncryptedCertificateData = encryption.Encrypt(Certificate), CreatedAt = Now, UpdatedAt = Now });
        db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.IsInitialized, Value = "true", UpdatedAt = Now });
        if (withRow)
            db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.EncryptionKey, Value = OldKey, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    private async Task<bool> AdoptAsync(string? configuredKey)
    {
        await using var db = NewContext();
        return await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(configuredKey), _logs);
    }

    private async Task<bool> RowExistsAsync()
    {
        await using var db = NewContext();
        return await db.SystemConfigs.AnyAsync(c => c.Key == SystemConfigKeys.EncryptionKey);
    }

    private async Task<List<(string Key, string Value)>> SystemConfigsAsync()
    {
        await using var db = NewContext();
        return (await db.SystemConfigs.AsNoTracking().OrderBy(c => c.Key).ToListAsync()).Select(c => (c.Key, c.Value)).ToList();
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
        var provider = await StartHostAsync(configuredKey);
        return provider.CreateScope().ServiceProvider.GetRequiredService<IEncryptionService>();
    }

    private async Task<ServiceProvider> StartHostAsync(string? configuredKey)
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
        return provider;
    }

    private async Task<Guid> AddTenantWithoutCredentialAsync()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewContext();
        db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "5260250274", CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> AddCorruptCredentialAsync()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await using var db = NewContext();
        db.Tenants.Add(new Tenant { Id = tenantId, UserId = $"user-{tenantId:N}", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
        db.KSeFCredentials.Add(new KSeFCredential { Id = id, TenantId = tenantId, EncryptedToken = Convert.ToBase64String(new byte[64]), CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<IActionResult> PostTokenAsync(ServiceProvider host, Guid tenantId, string token, ApplicationDbContext? db = null)
    {
        using var scope = host.CreateScope();
        var controller = new CredentialsController(
            db ?? scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new TestUser(),
            scope.ServiceProvider.GetRequiredService<IEncryptionService>(),
            null!,
            NullLogger<CredentialsController>.Instance);
        return await controller.AddOrUpdate(tenantId, new AddCredentialRequest(Type: CredentialType.Token, Token: token));
    }

    private async Task WaitUntilBlockedOrDoneAsync(Task other)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!other.IsCompleted && DateTime.UtcNow < deadline)
        {
            await using var db = NewContext();
            var blocked = await db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                """).SingleAsync();
            if (blocked > 0)
                return;
            await Task.Delay(50);
        }
    }

    private static bool CanDecrypt(byte[] key, string ciphertext)
    {
        try
        {
            new AesGcmEncryptionService(key).Decrypt(ciphertext);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private void AssertNoSecretsLogged()
    {
        foreach (var secret in new[] { OldKey, NewKey, MalformedKey, Token, Certificate, RelayKey, ClientSecret })
            Assert.DoesNotContain(_logs.Entries, e => e.Text.Contains(secret));
    }

    private const string RelayKey = "relay-api-key-plaintext";
    private const string ClientSecret = "client-secret-plaintext";
    private const string Firebase = "{\"type\":\"service_account\"}";

    private async Task SeedSecretsAsync(byte[] firebaseKey)
    {
        await using var db = NewContext();
        db.SystemConfigs.AddRange(
            new SystemConfig { Key = SystemConfigKeys.PushRelayApiKey, Value = RelayKey, UpdatedAt = Now },
            new SystemConfig { Key = SystemConfigKeys.ApiClientSecret, Value = ClientSecret, UpdatedAt = Now },
            new SystemConfig { Key = SystemConfigKeys.FirebaseCredentialsJson, Value = Encrypted(firebaseKey, Firebase), UpdatedAt = Now },
            new SystemConfig { Key = SystemConfigKeys.GoogleClientSecret, Value = "", UpdatedAt = Now },
            new SystemConfig { Key = SystemConfigKeys.ExternalBaseUrl, Value = "https://example.test", UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    private static string Encrypted(byte[] key, string plaintext) =>
        SystemConfigKeys.EncryptedPrefix + new AesGcmEncryptionService(key).Encrypt(plaintext);

    private async Task<string> RawAsync(string key)
    {
        await using var db = NewContext();
        return (await db.SystemConfigs.AsNoTracking().SingleAsync(c => c.Key == key)).Value;
    }

    private async Task AssertSecretsDecryptAsync(byte[] key)
    {
        var encryption = new AesGcmEncryptionService(key);
        foreach (var (name, plaintext) in new[] { (SystemConfigKeys.PushRelayApiKey, RelayKey), (SystemConfigKeys.ApiClientSecret, ClientSecret), (SystemConfigKeys.FirebaseCredentialsJson, Firebase) })
        {
            var raw = await RawAsync(name);
            Assert.StartsWith(SystemConfigKeys.EncryptedPrefix, raw);
            Assert.Equal(plaintext, encryption.Decrypt(raw[SystemConfigKeys.EncryptedPrefix.Length..]));
        }
        Assert.Equal("", await RawAsync(SystemConfigKeys.GoogleClientSecret));
        Assert.Equal("https://example.test", await RawAsync(SystemConfigKeys.ExternalBaseUrl));
    }

    [Fact]
    public async Task StartupPass_EncryptsPlaintextSecrets_LeavesEncryptedAndEmpty_SecondRunNoOp()
    {
        await SeedAsync(_newKey, withRow: false);
        await SeedSecretsAsync(_newKey);
        var firebaseBefore = await RawAsync(SystemConfigKeys.FirebaseCredentialsJson);

        Assert.True(await AdoptAsync(NewKey));

        await AssertSecretsDecryptAsync(_newKey);
        Assert.Equal(firebaseBefore, await RawAsync(SystemConfigKeys.FirebaseCredentialsJson));
        var info = Assert.Single(_logs.Entries, e => e.Text.Contains("system config secrets encrypted"));
        Assert.Contains(SystemConfigKeys.PushRelayApiKey, info.Text);
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
        AssertNoSecretsLogged();

        var afterFirst = await SystemConfigsAsync();
        Assert.True(await AdoptAsync(NewKey));
        Assert.Equal(afterFirst, await SystemConfigsAsync());
        Assert.Single(_logs.Entries, e => e.Text.Contains("system config secrets encrypted"));

        await using var host = await StartHostAsync(NewKey);
        var config = host.GetRequiredService<ISystemConfigService>();
        Assert.Equal(RelayKey, config.GetValue(SystemConfigKeys.PushRelayApiKey));
        Assert.Equal(ClientSecret, config.GetValue(SystemConfigKeys.ApiClientSecret));
        Assert.Equal(Firebase, config.GetValue(SystemConfigKeys.FirebaseCredentialsJson));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MalformedKey)]
    public async Task StartupPass_NoUsableKey_KeepsPlaintext_AndWarnsWithKeyNamesOnly(string? configuredKey)
    {
        await SeedAsync(_newKey, withRow: false);
        await SeedSecretsAsync(_newKey);
        var before = await SystemConfigsAsync();

        Assert.True(await AdoptAsync(configuredKey));

        Assert.Equal(before, await SystemConfigsAsync());
        var warning = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("stay in plaintext", warning.Text);
        Assert.Contains(SystemConfigKeys.ApiClientSecret, warning.Text);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task StartupPass_KeyDoesNotDecryptExistingData_KeepsPlaintext_UntilTheRightKeyIsBack()
    {
        await SeedAsync(_oldKey, withRow: false);
        await SeedSecretsAsync(_oldKey);
        var before = await SystemConfigsAsync();

        Assert.True(await AdoptAsync(NewKey));

        Assert.Equal(before, await SystemConfigsAsync());
        var warning = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("does not decrypt existing data", warning.Text);
        Assert.Contains(SystemConfigKeys.PushRelayApiKey, warning.Text);
        AssertNoSecretsLogged();

        Assert.True(await AdoptAsync(OldKey));

        await AssertSecretsDecryptAsync(_oldKey);
    }

    [Fact]
    public async Task StartupPass_TwoHostsConcurrently_EncryptOnce()
    {
        await SeedAsync(_newKey, withRow: false);
        await SeedSecretsAsync(_newKey);

        for (var round = 0; round < 10; round++)
        {
            if (round > 0)
            {
                await using var reset = NewContext();
                (await reset.SystemConfigs.SingleAsync(c => c.Key == SystemConfigKeys.PushRelayApiKey)).Value = RelayKey;
                (await reset.SystemConfigs.SingleAsync(c => c.Key == SystemConfigKeys.ApiClientSecret)).Value = ClientSecret;
                await reset.SaveChangesAsync();
            }

            var logs = new ListLogger();
            await Task.WhenAll(
                Task.Run(() => PassWithSlowSaveAsync(logs)),
                Task.Run(() => PassWithSlowSaveAsync(logs)));

            await AssertSecretsDecryptAsync(_newKey);
            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
            Assert.Single(logs.Entries, e => e.Text.Contains("system config secrets encrypted"));
        }
    }

    private async Task PassWithSlowSaveAsync(ILogger logger)
    {
        await using var db = NewContext(new BeforeSave(() => Task.Delay(50)));
        await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), logger);
    }

    [Fact]
    public async Task StartupPass_WizardWriteDuringPass_IsKept()
    {
        const string newRelayKey = "relay-api-key-new";
        await SeedAsync(_newKey, withRow: false);
        await SeedSecretsAsync(_newKey);
        await using var host = await StartHostAsync(NewKey);
        Task? write = null;
        var writeWaited = false;

        await using (var db = NewContext(new BeforeSave(async () =>
        {
            write = Task.Run(() => host.GetRequiredService<ISystemConfigService>().SetValueAsync(SystemConfigKeys.PushRelayApiKey, newRelayKey));
            await WaitUntilBlockedOrDoneAsync(write);
            writeWaited = !write.IsCompleted;
        })))
            Assert.True(await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs));
        await write!;

        Assert.True(writeWaited);
        Assert.Contains(_logs.Entries, e => e.Text.Contains("2 system config secrets encrypted"));

        var raw = await RawAsync(SystemConfigKeys.PushRelayApiKey);
        Assert.Equal(newRelayKey, new AesGcmEncryptionService(_newKey).Decrypt(raw[SystemConfigKeys.EncryptedPrefix.Length..]));
    }

    [Fact]
    public async Task Adoption_ReEncryptsSecretRows_UnderTheConfiguredKey()
    {
        await SeedAsync(_oldKey, withRow: true);
        await SeedSecretsAsync(_oldKey);

        Assert.True(await AdoptAsync(NewKey));

        Assert.False(await RowExistsAsync());
        await AssertSecretsDecryptAsync(_newKey);
        Assert.Contains(_logs.Entries, e => e.Text.Contains("2 credentials and 3 system config secrets re-encrypted"));
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task RotateCommand_ReEncryptsSecretRows_AndReRunIsIdempotent()
    {
        await SeedAsync(_oldKey, withRow: false);
        await SeedSecretsAsync(_oldKey);
        var output = new StringWriter();

        await using (var db = NewContext())
            Assert.Equal(0, await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), output));

        Assert.Equal("Re-encrypted 2 credentials and 3 system config secrets.", output.ToString().Trim());
        await AssertSecretsDecryptAsync(_newKey);
        var afterFirst = await SystemConfigsAsync();
        await using (var db = NewContext())
            Assert.Equal((0, 0), await EncryptionKeyMigration.RotateAsync(db, _oldKey, _newKey));
        Assert.Equal(afterFirst, await SystemConfigsAsync());

        await using var host = await StartHostAsync(NewKey);
        Assert.Equal(RelayKey, host.GetRequiredService<ISystemConfigService>().GetValue(SystemConfigKeys.PushRelayApiKey));
    }

    [Fact]
    public async Task SetValues_StoresSecretAsEnc_ReadsPlaintext()
    {
        await SeedAsync(_newKey, withRow: false);
        await using (var host = await StartHostAsync(NewKey))
            await host.GetRequiredService<ISystemConfigService>().SetValuesAsync(new Dictionary<string, string>
            {
                [SystemConfigKeys.PushRelayApiKey] = RelayKey,
                [SystemConfigKeys.GoogleClientSecret] = "",
                [SystemConfigKeys.ExternalBaseUrl] = "https://example.test",
            });

        var raw = await RawAsync(SystemConfigKeys.PushRelayApiKey);
        Assert.StartsWith(SystemConfigKeys.EncryptedPrefix, raw);
        Assert.DoesNotContain(RelayKey, raw);
        Assert.Equal("", await RawAsync(SystemConfigKeys.GoogleClientSecret));
        Assert.Equal("https://example.test", await RawAsync(SystemConfigKeys.ExternalBaseUrl));
        await using var restarted = await StartHostAsync(NewKey);
        Assert.Equal(RelayKey, restarted.GetRequiredService<ISystemConfigService>().GetValue(SystemConfigKeys.PushRelayApiKey));
    }

    [Fact]
    public async Task ValidEnv_MovesKeyOut_AndReEncryptsAll()
    {
        await SeedAsync(_oldKey, withRow: true);

        Assert.True(await AdoptAsync(NewKey));

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
        var warning = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("rotate-encryption-key", warning.Text);
        AssertNoSecretsLogged();
    }

    [Theory]
    [InlineData(null, "It is not set.")]
    [InlineData(MalformedKey, "Encryption key is not valid base64.")]
    public async Task RowPresent_NoOrMalformedKey_RefusalLogText(string? configuredKey, string reason)
    {
        await SeedAsync(_oldKey, withRow: true);

        await AdoptAsync(configuredKey);

        Assert.Equal(
            $"The encryption key is still stored in the database and the configured key cannot be used: {reason} " +
            "This version no longer reads the key from the database. Nothing changed. " +
            "Follow https://open-ksef.pl/docs/admin-setup/#aktualizacja-z-wersji-v007-lub-starszej: back up, " +
            "set ENCRYPTION_KEY on the API and Worker to a new key from 'openssl rand -base64 32', and restart. " +
            "To keep running meanwhile, go back to v0.0.14.",
            Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical).Text);
    }

    [Fact]
    public async Task ConfiguredKeyFileUnreadable_AndMalformedKey_LogDifferentReasons_WithoutKeyMaterial()
    {
        await SeedAsync(_oldKey, withRow: true);
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var malformedFile = Path.Combine(directory.FullName, "key");
            await File.WriteAllTextAsync(malformedFile, MalformedKey);

            foreach (var (path, reason) in new[] { (Path.Combine(directory.FullName, "missing"), "cannot be read"), (malformedFile, "not valid base64") })
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY_FILE"] = path })
                    .Build();
                var logs = new ListLogger();
                await using var db = NewContext();

                Assert.False(await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, configuration, logs));

                Assert.Contains(reason, Assert.Single(logs.Entries, e => e.Level == LogLevel.Critical).Text);
                Assert.DoesNotContain(logs.Entries, e => e.Text.Contains(MalformedKey) || e.Text.Contains(OldKey));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(MalformedKey)]
    [InlineData("generate_with_openssl_rand")]
    public async Task RowPresent_NoOrMalformedEnv_RefusesStart_WritesNothing(string? configuredKey)
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();
        var configBefore = await SystemConfigsAsync();

        Assert.False(await AdoptAsync(configuredKey));

        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        Assert.Equal(configBefore, await SystemConfigsAsync());
        var critical = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical).Text;
        Assert.Contains("no longer reads the key from the database", critical);
        Assert.Contains("ENCRYPTION_KEY", critical);
        Assert.DoesNotContain(_logs.Entries, e => e.Level == LogLevel.Error);
        await AssertAllDecryptAsync(_oldKey);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task ApiProcess_RowPresent_NoKey_ExitsOne_WritesNothing()
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();
        var configBefore = await SystemConfigsAsync();

        var (exitCode, output) = await RunApiAsync(currentKey: null, newKey: null, rotate: false);

        Assert.True(exitCode == 1, output);
        Assert.Contains("no longer reads the key from the database", output);
        Assert.Equal(before, await CiphertextAsync());
        Assert.Equal(configBefore, await SystemConfigsAsync());
        await AssertAllDecryptAsync(_oldKey);
    }

    [Fact]
    public async Task OneCorruptCiphertext_ChangesNothing_AndNamesTheCredential()
    {
        await SeedAsync(_oldKey, withRow: true);
        var corruptId = await AddCorruptCredentialAsync();
        var before = await CiphertextAsync();

        await AdoptAsync(NewKey);

        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        var critical = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(corruptId.ToString(), critical.Text);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task FailedAdoption_HostRunsOnEnvKey_NextAdoptionKeepsItsWrite()
    {
        await SeedAsync(_oldKey, withRow: true);
        var corruptId = await AddCorruptCredentialAsync();

        Assert.True(await AdoptAsync(NewKey));
        Assert.True(await RowExistsAsync());

        var newTenantId = await AddTenantWithoutCredentialAsync();
        await using (var host = await StartHostAsync(NewKey))
            Assert.IsType<OkObjectResult>(await PostTokenAsync(host, newTenantId, "user-new-token"));
        await using (var db = NewContext())
        {
            db.KSeFCredentials.Remove(await db.KSeFCredentials.SingleAsync(c => c.Id == corruptId));
            await db.SaveChangesAsync();
        }

        Assert.True(await AdoptAsync(NewKey));

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_newKey);
        await using var check = NewContext();
        var saved = await check.KSeFCredentials.SingleAsync(c => c.TenantId == newTenantId);
        Assert.Equal("user-new-token", new AesGcmEncryptionService(_newKey).Decrypt(saved.EncryptedToken!));
    }

    [Fact]
    public async Task TwoHostsAdoptConcurrently_OneWins_AndAllDecrypt()
    {
        await SeedAsync(_oldKey, withRow: true);

        for (var round = 0; round < 15; round++)
        {
            if (round > 0)
            {
                await using var reset = NewContext();
                foreach (var credential in await reset.KSeFCredentials.ToListAsync())
                {
                    var old = new AesGcmEncryptionService(_oldKey);
                    credential.EncryptedToken = credential.EncryptedToken is null ? null : old.Encrypt(Token);
                    credential.EncryptedCertificateData = credential.EncryptedCertificateData is null ? null : old.Encrypt(Certificate);
                }
                reset.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.EncryptionKey, Value = OldKey, UpdatedAt = Now });
                await reset.SaveChangesAsync();
            }

            var logs = new ListLogger();
            await Task.WhenAll(
                Task.Run(() => AdoptWithSlowSaveAsync(logs)),
                Task.Run(() => AdoptWithSlowSaveAsync(logs)));

            Assert.False(await RowExistsAsync());
            await AssertAllDecryptAsync(_newKey);
            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
            Assert.Single(logs.Entries, e => e.Text.Contains("re-encrypted"));
        }
    }

    private async Task AdoptWithSlowSaveAsync(ILogger logger)
    {
        await using var db = NewContext(new BeforeSave(() => Task.Delay(50)));
        await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), logger);
    }

    [Fact]
    public async Task TwoHostsWithDifferentKeys_LoserWaitsAtLock_AndWritesNothing()
    {
        await SeedAsync(_oldKey, withRow: true);
        var otherKey = RandomNumberGenerator.GetBytes(32);
        var winnerLogs = new ListLogger();
        var loserLogs = new ListLogger();
        Task? loser = null;

        await using (var db = NewContext(new BeforeSave(async () =>
        {
            loser = Task.Run(async () =>
            {
                await using var loserDb = NewContext();
                await EncryptionKeyMigration.AdoptConfiguredKeyAsync(loserDb, Config(Convert.ToBase64String(otherKey)), loserLogs);
            });
            await WaitUntilBlockedOrDoneAsync(loser);
        })))
            await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), winnerLogs);
        await loser!;

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_newKey);
        var (token, _) = await CiphertextAsync();
        Assert.False(CanDecrypt(otherKey, token));
        Assert.Contains(winnerLogs.Entries, e => e.Text.Contains("re-encrypted"));
        Assert.DoesNotContain(loserLogs.Entries, e => e.Text.Contains("re-encrypted"));
        Assert.Contains(_tokenCredentialId.ToString(), Assert.Single(loserLogs.Entries, e => e.Level == LogLevel.Critical).Text);
    }

    [Fact]
    public async Task HeldKeyRowLock_AdoptionTimesOut_LogsLockCritical_ChangesNothing()
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();
        await using var holder = NewContext();
        await using var held = await holder.Database.BeginTransactionAsync();
        await EncryptionKeyMigration.ReadKeyRowLockedAsync(holder);

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.CommandTimeout(2)).Options))
            Assert.True(await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs));

        await held.RollbackAsync();
        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        Assert.StartsWith(
            "Another database client holds the encryption_key row, so the key was not moved. Nothing changed. " +
            "Credentials stay unusable until it moves. " +
            "Find the client with the query in https://open-ksef.pl/docs/admin-setup/#blokada-przy-przenoszeniu-klucza, close it, and restart.",
            Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical).Text);
    }

    [Fact]
    public async Task WriteAfterAdoption_OpensNoTransaction()
    {
        await SeedAsync(_oldKey, withRow: false);
        var tenantId = await AddTenantWithoutCredentialAsync();
        await using var host = await StartHostAsync(OldKey);
        var transactions = 0;
        await using var db = NewContext(new OnTransactionStart(() => transactions++));

        Assert.IsType<OkObjectResult>(await PostTokenAsync(host, tenantId, "user-new-token", db));

        Assert.Equal(0, transactions);
    }

    [Fact]
    public async Task CredentialDeletedDuringAdoption_WaitsForTheAdopter_ThenDeletes()
    {
        await SeedAsync(_oldKey, withRow: true);
        Task? delete = null;

        await using (var db = NewContext(new BeforeSave(async () =>
        {
            delete = Task.Run(async () =>
            {
                await using var other = NewContext();
                await other.KSeFCredentials.Where(c => c.Id == _tokenCredentialId).ExecuteDeleteAsync();
            });
            await WaitUntilBlockedOrDoneAsync(delete);
            Assert.False(delete.IsCompleted);
        })))
            await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs);
        await delete!;

        Assert.False(await RowExistsAsync());
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
        await using var check = NewContext();
        Assert.Equal(_certificateCredentialId, (await check.KSeFCredentials.SingleAsync()).Id);
    }

    [Fact]
    public async Task CredentialUpdateInFlightDuringAdoption_IsKept_AndDecryptsWithTheConfiguredKey()
    {
        const string updatedToken = "ksef-token-updated";
        await SeedAsync(_oldKey, withRow: true);
        await using var writer = NewContext();
        await using var update = await writer.Database.BeginTransactionAsync();
        await writer.KSeFCredentials.Where(c => c.Id == _tokenCredentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EncryptedToken, new AesGcmEncryptionService(_newKey).Encrypt(updatedToken)));

        var adoption = Task.Run(() => AdoptAsync(NewKey));
        await WaitUntilBlockedOrDoneAsync(adoption);
        await update.CommitAsync();
        Assert.True(await adoption);

        Assert.False(await RowExistsAsync());
        var (token, certificate) = await CiphertextAsync();
        var encryption = new AesGcmEncryptionService(_newKey);
        Assert.Equal(updatedToken, encryption.Decrypt(token));
        Assert.Equal(Certificate, encryption.Decrypt(certificate));
    }

    [Fact]
    public async Task HeldCredentialLock_AdoptionTimesOut_LogsCredentialLockCritical_ChangesNothing()
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();
        await using var holder = NewContext();
        await using var held = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync("""SELECT 1 FROM "KSeFCredentials" FOR UPDATE""");

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.CommandTimeout(2)).Options))
            Assert.True(await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs));

        await held.RollbackAsync();
        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
        Assert.StartsWith(
            "Moving the encryption key out of the database timed out waiting for a lock on KSeFCredentials. Nothing changed. " +
            "Find the lock holder with the query at https://open-ksef.pl/docs/admin-setup/#blokada-przy-przenoszeniu-klucza and restart API and Worker.",
            Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical).Text);
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
    public async Task RowAbsent_RightKey_StartsAsBefore_ChangesNothing()
    {
        await SeedAsync(_newKey, withRow: false);
        var before = await CiphertextAsync();
        var configBefore = await SystemConfigsAsync();

        Assert.True(await AdoptAsync(NewKey));

        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(before, await CiphertextAsync());
        Assert.Equal(configBefore, await SystemConfigsAsync());
        var encryption = await ResolveAsHostAsync(NewKey);
        Assert.Equal(Token, encryption.Decrypt(before.Token));
        Assert.Equal(Certificate, encryption.Decrypt(before.Certificate));
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

        Assert.True(await AdoptAsync(configuredKey));

        var critical = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Critical).Text;
        Assert.Contains("Restore the key that encrypted the credentials", critical);
        Assert.Contains("only if no copy remains", critical);
        Assert.DoesNotContain("openssl rand", critical);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Rotate_ReEncryptsAll_AndReRunIsIdempotent()
    {
        await SeedAsync(_oldKey, withRow: false);

        await using (var db = NewContext())
            Assert.Equal((2, 0), await EncryptionKeyMigration.RotateAsync(db, _oldKey, _newKey));
        await AssertAllDecryptAsync(_newKey);
        var afterFirst = await CiphertextAsync();

        await using (var db = NewContext())
            Assert.Equal((0, 0), await EncryptionKeyMigration.RotateAsync(db, _oldKey, _newKey));
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
        Assert.Equal("Re-encrypted 2 credentials and 0 system config secrets.", output.ToString().Trim());
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

        foreach (var (configuration, expected, unexpected) in new[]
                 {
                     (Config(MalformedKey, NewKey), "Restore the key that encrypted the credentials", "openssl rand"),
                     (Config(OldKey, MalformedKey), "openssl rand", "Restore the key"),
                 })
        {
            var output = new StringWriter();
            await using var db = NewContext();
            Assert.Equal(1, await EncryptionKeyMigration.RunRotateCommandAsync(db, configuration, output));
            Assert.Contains(expected, output.ToString());
            Assert.DoesNotContain(unexpected, output.ToString());
        }

        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_CorruptCredential_ExitsOne_NothingWritten()
    {
        await SeedAsync(_oldKey, withRow: false);
        await AddCorruptCredentialAsync();
        var before = await CiphertextAsync();

        await using var rotateDb = NewContext();
        var exitCode = await EncryptionKeyMigration.RunRotateCommandAsync(rotateDb, Config(OldKey, NewKey), new StringWriter());

        Assert.Equal(1, exitCode);
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_UnnamedClientConnected_Refuses_AndPrintsTheCount()
    {
        await SeedAsync(_oldKey, withRow: false);
        var before = await CiphertextAsync();
        await using var runningHost = new NpgsqlConnection(_postgres.GetConnectionString() + ";Pooling=false");
        await runningHost.OpenAsync();
        var output = new StringWriter();

        await using var db = NewContext();
        var exitCode = await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("1 other connections", output.ToString());
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task RotateCommand_OwnPool_DoesNotCount()
    {
        await SeedAsync(_oldKey, withRow: false);
        var contexts = Enumerable.Range(0, 5).Select(_ => NewContext()).ToList();
        foreach (var context in contexts)
            await context.Database.OpenConnectionAsync();
        foreach (var context in contexts)
            await context.DisposeAsync();

        await using var db = NewContext();

        Assert.Equal(0, await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), new StringWriter()));
    }

    [Fact]
    public async Task RotateCommand_NamedTool_DoesNotCount()
    {
        await SeedAsync(_oldKey, withRow: false);
        await using var psql = new NpgsqlConnection(_postgres.GetConnectionString() + ";Pooling=false;Application Name=psql");
        await psql.OpenAsync();

        await using var db = NewContext();

        Assert.Equal(0, await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), new StringWriter()));
    }

    [Fact]
    public async Task RotateCommand_AutovacuumRunning_DoesNotCount()
    {
        await SeedAsync(_oldKey, withRow: false);
        await using (var setup = NewContext())
        {
            await setup.Database.ExecuteSqlRawAsync("ALTER SYSTEM SET autovacuum_naptime = 1");
            await setup.Database.ExecuteSqlRawAsync("SELECT pg_reload_conf()");
            await setup.Database.ExecuteSqlRawAsync("CREATE TABLE vacuum_me AS SELECT g FROM generate_series(1, 200000) g");
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE vacuum_me SET (autovacuum_vacuum_threshold = 0, autovacuum_vacuum_scale_factor = 0, autovacuum_vacuum_cost_delay = 100, autovacuum_vacuum_cost_limit = 1)");
            await setup.Database.ExecuteSqlRawAsync("DELETE FROM vacuum_me");
        }

        async Task<bool> AutovacuumRunningAsync()
        {
            await using var db = NewContext();
            return await db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND backend_type = 'autovacuum worker'
                """).SingleAsync() > 0;
        }

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await AutovacuumRunningAsync() && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        Assert.True(await AutovacuumRunningAsync());

        await using (var rotateDb = NewContext())
            Assert.Equal(0, await EncryptionKeyMigration.RunRotateCommandAsync(rotateDb, Config(OldKey, NewKey), new StringWriter()));

        Assert.True(await AutovacuumRunningAsync());
    }

    [Fact]
    public async Task RotateCommand_PendingMigrations_Refuses_AndCreatesNothing()
    {
        await using var db = NewContext();

        Assert.Equal(1, await EncryptionKeyMigration.RunRotateCommandAsync(db, Config(OldKey, NewKey), new StringWriter()));

        await using var check = NewContext();
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            """SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_schema = 'public'""").SingleAsync());
    }

    [Theory]
    [InlineData(true, "still in the database")]
    [InlineData(false, "ENCRYPTION_KEY_NEW is not set")]
    public async Task ApiProcess_RotateWithRowPresent_ExitsOne_WritesNothing(bool withNewKey, string reason)
    {
        await SeedAsync(_oldKey, withRow: true);
        var before = await CiphertextAsync();

        var (exitCode, output) = await RunApiAsync(NewKey, withNewKey ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : null);

        Assert.Equal(1, exitCode);
        Assert.Contains(reason, output);
        Assert.True(await RowExistsAsync());
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task ApiProcess_RotateWithRunningHost_ExitsOne_WritesNothing()
    {
        await SeedAsync(_oldKey, withRow: false);
        var before = await CiphertextAsync();
        await using var runningHost = new NpgsqlConnection(_postgres.GetConnectionString() + ";Pooling=false");
        await runningHost.OpenAsync();

        var (exitCode, output) = await RunApiAsync(OldKey, NewKey);

        Assert.Equal(1, exitCode);
        Assert.Contains("1 other connections", output);
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task ApiProcess_RotateAfterAdoption_ExitsZero_AndRotates()
    {
        await SeedAsync(_oldKey, withRow: false);

        var (exitCode, output) = await RunApiAsync(OldKey, NewKey);

        Assert.True(exitCode == 0, output);
        Assert.Contains("Re-encrypted 2 credentials and 0 system config secrets.", output);
        Assert.DoesNotContain(OldKey, output);
        Assert.DoesNotContain(NewKey, output);
        await AssertAllDecryptAsync(_newKey);
    }

    private async Task<(int ExitCode, string Output)> RunApiAsync(string? currentKey, string? newKey, bool rotate = true)
    {
        await using (var ownPool = new NpgsqlConnection(_postgres.GetConnectionString()))
            NpgsqlConnection.ClearPool(ownPool);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(CredentialsController).Assembly.Location);
        if (rotate)
            start.ArgumentList.Add("rotate-encryption-key");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__Db"] = _postgres.GetConnectionString();
        start.Environment["Auth__Authority"] = "http://localhost:1/auth/realms/openksef";
        start.Environment["Auth__RequireHttpsMetadata"] = "false";
        start.Environment["ENCRYPTION_KEY"] = currentKey ?? "";
        start.Environment["ENCRYPTION_KEY_NEW"] = newKey ?? "";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class TestUser : ICurrentUserService
    {
        public string UserId => "user-1";
    }

    private sealed class BeforeSave(Func<Task> before) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await before();
            return result;
        }
    }

    private sealed class OnTransactionStart(Action onStart) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            onStart();
            return ValueTask.FromResult(result);
        }
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
