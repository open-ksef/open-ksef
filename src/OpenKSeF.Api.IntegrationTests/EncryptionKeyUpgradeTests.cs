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

    private static async Task<IActionResult> PostTokenAsync(ServiceProvider host, Guid tenantId, string token)
    {
        using var scope = host.CreateScope();
        var controller = new CredentialsController(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new TestUser(),
            scope.ServiceProvider.GetRequiredService<IEncryptionService>(),
            null!,
            scope.ServiceProvider.GetRequiredService<ISystemConfigService>(),
            NullLogger<CredentialsController>.Instance);
        return await controller.AddOrUpdate(tenantId, new AddCredentialRequest(Type: CredentialType.Token, Token: token));
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
        var warning = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("rotate-encryption-key", warning.Text);
        AssertNoSecretsLogged();
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

                await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, configuration, logs);

                Assert.Contains(reason, Assert.Single(logs.Entries, e => e.Level == LogLevel.Error).Text);
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
            var bothRead = new Barrier(2);
            await Task.WhenAll(
                Task.Run(() => AdoptAfterBarrierAsync(bothRead, logs)),
                Task.Run(() => AdoptAfterBarrierAsync(bothRead, logs)));

            Assert.False(await RowExistsAsync());
            await AssertAllDecryptAsync(_newKey);
            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
            Assert.Single(logs.Entries, e => e.Text.Contains("re-encrypted"));
            Assert.Single(logs.Entries, e => e.Text.Contains("by another host"));
        }
    }

    private async Task AdoptAfterBarrierAsync(Barrier bothRead, ILogger logger)
    {
        await using var db = NewContext(new BeforeSave(() =>
        {
            bothRead.SignalAndWait();
            return Task.CompletedTask;
        }));
        await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), logger);
    }

    [Fact]
    public async Task TwoHostsWithDifferentKeys_LoserBlocksOnWinner_AndRollsBack()
    {
        await SeedAsync(_oldKey, withRow: true);
        var otherKey = RandomNumberGenerator.GetBytes(32);
        var bothRead = new Barrier(2);
        var loserWrites = new TaskCompletionSource();
        var winnerLogs = new ListLogger();
        var loserLogs = new ListLogger();

        await Task.WhenAll(
            Task.Run(async () =>
            {
                await using var db = NewContext(
                    new BeforeSave(() =>
                    {
                        bothRead.SignalAndWait();
                        return Task.CompletedTask;
                    }),
                    new BeforeCommit(async () =>
                    {
                        await loserWrites.Task;
                        await Task.Delay(1000);
                    }));
                await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), winnerLogs);
            }),
            Task.Run(async () =>
            {
                await using var db = NewContext(
                    new BeforeSave(async () =>
                    {
                        bothRead.SignalAndWait();
                        await Task.Delay(300);
                    }),
                    new OnWrite(() => loserWrites.TrySetResult()));
                await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(Convert.ToBase64String(otherKey)), loserLogs);
            }));

        Assert.False(await RowExistsAsync());
        await AssertAllDecryptAsync(_newKey);
        var (token, _) = await CiphertextAsync();
        Assert.False(CanDecrypt(otherKey, token));
        Assert.Contains(winnerLogs.Entries, e => e.Text.Contains("re-encrypted"));
        Assert.Contains(loserLogs.Entries, e => e.Text.Contains("by another host"));
    }

    [Fact]
    public async Task StaleHost_AfterOtherHostAdopted_Gets503_ThenWritesUnderConfiguredKey()
    {
        await SeedAsync(_oldKey, withRow: true);
        var tenantId = await AddTenantWithoutCredentialAsync();
        await using var staleHost = await StartHostAsync(NewKey);

        await AdoptAsync(NewKey);

        var refused = await PostTokenAsync(staleHost, tenantId, "user-new-token");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(refused).StatusCode);
        await using (var db = NewContext())
            Assert.False(await db.KSeFCredentials.AnyAsync(c => c.TenantId == tenantId));

        Assert.IsType<OkObjectResult>(await PostTokenAsync(staleHost, tenantId, "user-new-token"));
        await using var check = NewContext();
        var saved = await check.KSeFCredentials.SingleAsync(c => c.TenantId == tenantId);
        Assert.Equal("user-new-token", new AesGcmEncryptionService(_newKey).Decrypt(saved.EncryptedToken!));
    }

    [Fact]
    public async Task HostOnRowKey_NextScopeAfterRefresh_UsesConfiguredKey()
    {
        await SeedAsync(_oldKey, withRow: true);
        var (tokenBefore, _) = await CiphertextAsync();
        await using var host = await StartHostAsync(NewKey);
        using (var scope = host.CreateScope())
            Assert.Equal(Token, scope.ServiceProvider.GetRequiredService<IEncryptionService>().Decrypt(tokenBefore));

        await AdoptAsync(NewKey);
        await host.GetRequiredService<ISystemConfigService>().RefreshCacheAsync();

        var (tokenAfter, _) = await CiphertextAsync();
        using var nextScope = host.CreateScope();
        Assert.Equal(Token, nextScope.ServiceProvider.GetRequiredService<IEncryptionService>().Decrypt(tokenAfter));
    }

    // Accepted residual race: the row stays visible until the adopter commits, so the host's check passes. These pin the size of the gap.
    [Fact]
    public async Task RowKeyInsert_BetweenAdoptionReadAndCommit_IsStranded_AcceptedResidual()
    {
        await SeedAsync(_oldKey, withRow: true);
        var tenantId = await AddTenantWithoutCredentialAsync();
        await using var staleHost = await StartHostAsync(NewKey);

        await using (var db = NewContext(new BeforeSave(() => PostTokenAsync(staleHost, tenantId, "user-new-token"))))
            await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs);

        Assert.False(await RowExistsAsync());
        await using var check = NewContext();
        var stranded = await check.KSeFCredentials.SingleAsync(c => c.TenantId == tenantId);
        Assert.False(CanDecrypt(_newKey, stranded.EncryptedToken!));
        Assert.True(CanDecrypt(_oldKey, stranded.EncryptedToken!));
    }

    [Fact]
    public async Task RowKeyUpdate_BetweenAdoptionReadAndCommit_IsOverwritten_AcceptedResidual()
    {
        await SeedAsync(_oldKey, withRow: true);
        await using var staleHost = await StartHostAsync(NewKey);
        Guid tenantId;
        await using (var lookup = NewContext())
        {
            tenantId = (await lookup.KSeFCredentials.SingleAsync(c => c.Id == _tokenCredentialId)).TenantId;
            lookup.KSeFCredentials.Remove(await lookup.KSeFCredentials.SingleAsync(c => c.Id == _certificateCredentialId));
            await lookup.SaveChangesAsync();
        }

        await using (var db = NewContext(new BeforeSave(() => PostTokenAsync(staleHost, tenantId, "user-new-token"))))
            await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs);

        Assert.False(await RowExistsAsync());
        await using var check = NewContext();
        var overwritten = await check.KSeFCredentials.SingleAsync(c => c.Id == _tokenCredentialId);
        Assert.Equal(Token, new AesGcmEncryptionService(_newKey).Decrypt(overwritten.EncryptedToken!));
    }

    [Fact]
    public async Task CredentialDeletedDuringAdoption_LogsWarning_RowStays_NextStartAdopts()
    {
        await SeedAsync(_oldKey, withRow: true);

        await using (var db = NewContext(new BeforeSave(async () =>
        {
            await using var other = NewContext();
            other.KSeFCredentials.Remove(await other.KSeFCredentials.SingleAsync(c => c.Id == _tokenCredentialId));
            await other.SaveChangesAsync();
        })))
            await EncryptionKeyMigration.AdoptConfiguredKeyAsync(db, Config(NewKey), _logs);

        Assert.True(await RowExistsAsync());
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("next start"));
        Assert.DoesNotContain(_logs.Entries, e => e.Text.Contains("by another host"));

        await AdoptAsync(NewKey);

        Assert.False(await RowExistsAsync());
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

        var (exitCode, output) = await RunApiRotateAsync(NewKey, withNewKey ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : null);

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

        var (exitCode, output) = await RunApiRotateAsync(OldKey, NewKey);

        Assert.Equal(1, exitCode);
        Assert.Contains("1 other connections", output);
        Assert.Equal(before, await CiphertextAsync());
    }

    [Fact]
    public async Task ApiProcess_RotateAfterAdoption_ExitsZero_AndRotates()
    {
        await SeedAsync(_oldKey, withRow: false);

        var (exitCode, output) = await RunApiRotateAsync(OldKey, NewKey);

        Assert.True(exitCode == 0, output);
        Assert.Contains("Re-encrypted 2 credentials.", output);
        Assert.DoesNotContain(OldKey, output);
        Assert.DoesNotContain(NewKey, output);
        await AssertAllDecryptAsync(_newKey);
    }

    private async Task<(int ExitCode, string Output)> RunApiRotateAsync(string currentKey, string? newKey)
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
        start.ArgumentList.Add("rotate-encryption-key");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__Db"] = _postgres.GetConnectionString();
        start.Environment["Auth__Authority"] = "http://localhost:1/auth/realms/openksef";
        start.Environment["Auth__RequireHttpsMetadata"] = "false";
        start.Environment["ENCRYPTION_KEY"] = currentKey;
        start.Environment["ENCRYPTION_KEY_NEW"] = newKey ?? "";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
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

    private sealed class BeforeCommit(Func<Task> before) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            await before();
            return result;
        }
    }

    private sealed class OnWrite(Action onWrite) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
                onWrite();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
                onWrite();
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
