using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class DashboardServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 2, 27, 12, 0, 0, TimeSpan.Zero));

    public DashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetTenantOverviewAsync_ReturnsTenantScopedCountsAndSyncStatus()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedTenants(tenantA, tenantB, tenantForeign);
        SeedSyncStates(tenantA, tenantB);
        SeedInvoices(tenantA, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA, tenantB]);
        var service = new DashboardService(_db, resolver, _timeProvider);

        var result = await service.GetTenantOverviewAsync();

        Assert.Equal(2, result.Count);

        var first = Assert.Single(result.Where(r => r.TenantId == tenantA));
        Assert.Equal(3, first.TotalInvoices);
        Assert.Equal(1, first.InvoicesLast7Days);
        Assert.Equal(2, first.InvoicesLast30Days);
        Assert.Equal(SyncHealthStatus.Success, first.SyncStatus);

        var second = Assert.Single(result.Where(r => r.TenantId == tenantB));
        Assert.Equal(0, second.TotalInvoices);
        Assert.Equal(0, second.InvoicesLast7Days);
        Assert.Equal(0, second.InvoicesLast30Days);
        Assert.Equal(SyncHealthStatus.Error, second.SyncStatus);
    }

    [Fact]
    public async Task GetTenantOverviewAsync_StaleSync_SetsWarningStatus()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        SeedTenants(tenantA, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        _db.SyncStates.Add(new SyncState
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            LastSyncedAt = new DateTime(2026, 2, 25, 10, 0, 0, DateTimeKind.Utc),
            LastSuccessfulSync = new DateTime(2026, 2, 25, 10, 0, 0, DateTimeKind.Utc)
        });
        _db.SaveChanges();

        var resolver = new FakeTenantResolver([tenantA]);
        var service = new DashboardService(_db, resolver, _timeProvider);

        var result = await service.GetTenantOverviewAsync();

        var item = Assert.Single(result);
        Assert.Equal(SyncHealthStatus.Warning, item.SyncStatus);
    }

    [Fact]
    public async Task GetTenantOverviewAsync_NoTenants_ReturnsEmpty()
    {
        var resolver = new FakeTenantResolver([]);
        var service = new DashboardService(_db, resolver, _timeProvider);

        var result = await service.GetTenantOverviewAsync();

        Assert.Empty(result);
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
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Tenant
            {
                Id = tenantB,
                UserId = "user-a",
                Nip = "2222222222",
                DisplayName = "Tenant B",
                CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new Tenant
            {
                Id = tenantForeign,
                UserId = "user-b",
                Nip = "3333333333",
                DisplayName = "Tenant C",
                CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
            });

        _db.SaveChanges();
    }

    private void SeedSyncStates(Guid tenantA, Guid tenantB)
    {
        _db.SyncStates.AddRange(
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                LastSyncedAt = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Utc),
                LastSuccessfulSync = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Utc)
            },
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                LastSyncedAt = null,
                LastSuccessfulSync = null
            });

        _db.SaveChanges();
    }

    private void SeedInvoices(Guid tenantA, Guid tenantForeign)
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice(tenantA, new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc), "INV-1"),
            MakeInvoice(tenantA, new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc), "INV-2"),
            MakeInvoice(tenantA, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), "INV-3"),
            MakeInvoice(tenantForeign, new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc), "INV-4"));

        _db.SaveChanges();
    }

    private static InvoiceHeader MakeInvoice(Guid tenantId, DateTime issueDate, string number) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = number + "-ref",
            VendorName = "Vendor",
            VendorNip = "9876543210",
            AmountGross = 100m,
            Currency = "PLN",
            IssueDate = issueDate,
            FirstSeenAt = issueDate,
            LastUpdatedAt = issueDate
        };

    public void Dispose() => _db.Dispose();

    private sealed class FakeTenantResolver(List<Guid> tenantIds) : ITenantResolver
    {
        public string? GetCurrentUserId() => "user-a";

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(tenantIds);

        public Task<Guid?> GetCurrentTenantIdAsync() =>
            Task.FromResult<Guid?>(tenantIds.Count > 0 ? tenantIds[0] : null);

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) =>
            Task.FromResult(tenantIds.Contains(tenantId));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
