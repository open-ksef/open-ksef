using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class CredentialStatusServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 2, 27, 12, 0, 0, TimeSpan.Zero));

    public CredentialStatusServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetStatusesAsync_ReturnsOnlyCurrentUserTenantRows()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedTenants(tenantA, tenantB, tenantForeign);
        SeedCredentials(tenantA, tenantForeign);
        SeedSyncStates(tenantA, tenantB, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA, tenantB]);
        var service = new CredentialStatusService(_db, resolver, _timeProvider);

        var rows = await service.GetStatusesAsync();

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.TenantId == tenantForeign);
    }

    [Fact]
    public async Task GetStatusesAsync_MissingCredential_ReturnsErrorStatus()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SeedTenants(tenantA, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var resolver = new FakeTenantResolver([tenantA]);
        var service = new CredentialStatusService(_db, resolver, _timeProvider);

        var rows = await service.GetStatusesAsync();

        var row = Assert.Single(rows);
        Assert.False(row.TokenConfigured);
        Assert.Equal(CredentialHealthStatus.Error, row.Status);
    }

    [Fact]
    public async Task GetStatusesAsync_RecentSync_ReturnsActiveStatus()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SeedTenants(tenantA, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        _db.KSeFCredentials.Add(new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            EncryptedToken = "ciphertext",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SyncStates.Add(new SyncState
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            LastSuccessfulSync = new DateTime(2026, 2, 27, 9, 0, 0, DateTimeKind.Utc)
        });
        _db.SaveChanges();

        var resolver = new FakeTenantResolver([tenantA]);
        var service = new CredentialStatusService(_db, resolver, _timeProvider);

        var rows = await service.GetStatusesAsync();

        var row = Assert.Single(rows);
        Assert.True(row.TokenConfigured);
        Assert.Equal(CredentialHealthStatus.Active, row.Status);
    }

    private void SeedTenants(Guid tenantA, Guid tenantB, Guid tenantForeign)
    {
        _db.Tenants.AddRange(
            new Tenant
            {
                Id = tenantA,
                UserId = "user-a",
                Nip = "1111111111",
                DisplayName = "Tenant A",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = tenantB,
                UserId = "user-a",
                Nip = "2222222222",
                DisplayName = "Tenant B",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = tenantForeign,
                UserId = "user-b",
                Nip = "3333333333",
                DisplayName = "Tenant C",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        _db.SaveChanges();
    }

    private void SeedCredentials(Guid tenantA, Guid tenantForeign)
    {
        _db.KSeFCredentials.AddRange(
            new KSeFCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                EncryptedToken = "cipher-a",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new KSeFCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantForeign,
                EncryptedToken = "cipher-c",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        _db.SaveChanges();
    }

    private void SeedSyncStates(Guid tenantA, Guid tenantB, Guid tenantForeign)
    {
        _db.SyncStates.AddRange(
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                LastSuccessfulSync = new DateTime(2026, 2, 27, 8, 0, 0, DateTimeKind.Utc)
            },
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                LastSuccessfulSync = new DateTime(2026, 2, 25, 8, 0, 0, DateTimeKind.Utc)
            },
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantForeign,
                LastSuccessfulSync = new DateTime(2026, 2, 27, 8, 0, 0, DateTimeKind.Utc)
            });

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeTenantResolver(List<Guid> tenantIds) : ITenantResolver
    {
        public string? GetCurrentUserId() => "user-a";

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(tenantIds);

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(tenantIds.FirstOrDefault());

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(tenantIds.Contains(tenantId));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
