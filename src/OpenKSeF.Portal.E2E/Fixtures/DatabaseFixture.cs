using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using Respawn;

namespace OpenKSeF.Portal.E2E.Fixtures;

public sealed class DatabaseFixture : IAsyncDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly DbConnection? _sharedConnection;
    private readonly Respawner? _respawner;
    private readonly bool _canUseRespawn;

    private DatabaseFixture(
        DbContextOptions<ApplicationDbContext> options,
        DbConnection? sharedConnection,
        Respawner? respawner,
        bool canUseRespawn)
    {
        _options = options;
        _sharedConnection = sharedConnection;
        _respawner = respawner;
        _canUseRespawn = canUseRespawn;
    }

    public IDictionary<string, string> References { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static async Task<DatabaseFixture> CreateAsync(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        DbConnection? sharedConnection = null;
        Respawner? respawner = null;
        var canUseRespawn = false;

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseNpgsql(connectionString);
            canUseRespawn = true;
        }
        else if (connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            sharedConnection = new SqliteConnection(connectionString);
            await sharedConnection.OpenAsync();
            optionsBuilder.UseSqlite(sharedConnection);
        }
        else
        {
            optionsBuilder.UseSqlite(connectionString);
        }

        var options = optionsBuilder.Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (canUseRespawn)
        {
            await using var db = new ApplicationDbContext(options);
            await db.Database.OpenConnectionAsync();
            respawner = await Respawner.CreateAsync(db.Database.GetDbConnection(), new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"]
            });
            await db.Database.CloseConnectionAsync();
        }

        return new DatabaseFixture(options, sharedConnection, respawner, canUseRespawn);
    }

    public ApplicationDbContext CreateDbContext() => new(_options);

    public string CreateTestUser(string username, string password)
    {
        var userId = $"e2e-{username}";
        StoreReference("TestUserId", userId);
        StoreReference("TestUserPassword", password);
        return userId;
    }

    public async Task<Guid> CreateTestTenantAsync(string userId, string nip, string name)
    {
        await using var db = CreateDbContext();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Nip = nip,
            DisplayName = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        StoreReference("TenantId", tenant.Id.ToString());
        return tenant.Id;
    }

    public async Task<Guid> CreateTestCredentialAsync(Guid tenantId, string encryptedToken)
    {
        await using var db = CreateDbContext();

        var credential = new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = CredentialType.Token,
            EncryptedToken = encryptedToken,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.KSeFCredentials.Add(credential);
        await db.SaveChangesAsync();

        StoreReference("CredentialId", credential.Id.ToString());
        return credential.Id;
    }

    public async Task<Guid> CreateTestCertificateCredentialAsync(
        Guid tenantId, string encryptedCertData, string fingerprint)
    {
        await using var db = CreateDbContext();

        var credential = new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = CredentialType.Certificate,
            EncryptedCertificateData = encryptedCertData,
            CertificateFingerprint = fingerprint,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.KSeFCredentials.Add(credential);
        await db.SaveChangesAsync();

        StoreReference("CredentialId", credential.Id.ToString());
        return credential.Id;
    }

    public async Task<IReadOnlyList<Guid>> CreateTestInvoicesAsync(Guid tenantId, int count)
    {
        await using var db = CreateDbContext();

        var invoices = new List<InvoiceHeader>();
        for (var i = 0; i < count; i++)
        {
            invoices.Add(new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                KSeFInvoiceNumber = $"E2E-KSEF-{i + 1:000}",
                KSeFReferenceNumber = $"E2E-REF-{i + 1:000}",
                VendorName = "E2E Vendor",
                VendorNip = "1234567890",
                AmountGross = 100m + i,
                Currency = "PLN",
                IssueDate = DateTime.UtcNow.Date.AddDays(-i),
                FirstSeenAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            });
        }

        db.InvoiceHeaders.AddRange(invoices);
        await db.SaveChangesAsync();

        StoreReference("InvoiceCount", count.ToString());
        return invoices.Select(i => i.Id).ToList();
    }

    public async Task SeedDefaultDataAsync()
    {
        var userId = CreateTestUser("portal-e2e", "portal-e2e-password");
        var tenantId = await CreateTestTenantAsync(userId, "1234567890", "Portal E2E Tenant");
        await CreateTestCredentialAsync(tenantId, "encrypted-e2e-token");
        await CreateTestInvoicesAsync(tenantId, 5);
    }

    public async Task SeedSystemInitializedAsync()
    {
        await using var db = CreateDbContext();
        db.SystemConfigs.Add(new SystemConfig
        {
            Key = "is_initialized",
            Value = "true",
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task CleanupAsync()
    {
        if (_canUseRespawn && _respawner is not null)
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync();
            await _respawner.ResetAsync(db.Database.GetDbConnection());
            await db.Database.CloseConnectionAsync();
            References.Clear();
            return;
        }

        await using var fallbackDb = CreateDbContext();
        fallbackDb.KSeFCredentials.RemoveRange(fallbackDb.KSeFCredentials);
        fallbackDb.InvoiceHeaders.RemoveRange(fallbackDb.InvoiceHeaders);
        fallbackDb.SyncStates.RemoveRange(fallbackDb.SyncStates);
        fallbackDb.DeviceTokens.RemoveRange(fallbackDb.DeviceTokens);
        fallbackDb.Tenants.RemoveRange(fallbackDb.Tenants);
        fallbackDb.SystemConfigs.RemoveRange(fallbackDb.SystemConfigs);
        await fallbackDb.SaveChangesAsync();
        References.Clear();
    }

    public ValueTask DisposeAsync()
    {
        return _sharedConnection is null
            ? ValueTask.CompletedTask
            : _sharedConnection.DisposeAsync();
    }

    private void StoreReference(string key, string value)
    {
        References[key] = value;
        TestContext.Progress.WriteLine($"[seed-ref] {key}={value}");
    }
}
