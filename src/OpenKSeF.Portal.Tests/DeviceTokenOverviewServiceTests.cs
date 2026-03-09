using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class DeviceTokenOverviewServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public DeviceTokenOverviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCurrentUserDevices_WithMaskedTokens()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedTenants(tenantA, tenantB, tenantForeign);
        SeedDeviceTokens(tenantA, tenantB, tenantForeign);

        var resolver = new FakeTenantResolver("user-a", [tenantA, tenantB]);
        var service = new DeviceTokenOverviewService(_db, resolver);

        var rows = await service.ListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.DoesNotContain("-token-", row.TokenMasked, StringComparison.Ordinal));
        Assert.Equal("aaaaaaaaaa...", rows[0].TokenMasked);
        Assert.Equal("1234567890...", rows[1].TokenMasked);
        Assert.Equal(Platform.Android, rows[0].Platform);
        Assert.Equal(Platform.iOS, rows[1].Platform);
    }

    [Fact]
    public async Task ListAsync_WithTenantFilter_ReturnsOnlySelectedTenant()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        SeedTenants(tenantA, tenantB, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        SeedDeviceTokens(tenantA, tenantB, Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var resolver = new FakeTenantResolver("user-a", [tenantA, tenantB]);
        var service = new DeviceTokenOverviewService(_db, resolver);

        var rows = await service.ListAsync(tenantB);

        var row = Assert.Single(rows);
        Assert.Equal(tenantB, row.TenantId);
    }

    [Fact]
    public async Task ListAsync_WithForeignTenantFilter_ReturnsEmpty()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedTenants(tenantA, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), tenantForeign);
        SeedDeviceTokens(tenantA, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), tenantForeign);

        var resolver = new FakeTenantResolver("user-a", [tenantA]);
        var service = new DeviceTokenOverviewService(_db, resolver);

        var rows = await service.ListAsync(tenantForeign);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ListAsync_WhenUnauthenticated_ReturnsEmpty()
    {
        var service = new DeviceTokenOverviewService(_db, new FakeTenantResolver(null, []));

        var rows = await service.ListAsync();

        Assert.Empty(rows);
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

    private void SeedDeviceTokens(Guid tenantA, Guid tenantB, Guid tenantForeign)
    {
        _db.DeviceTokens.AddRange(
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = "user-a",
                Token = "1234567890-token-a",
                Platform = Platform.iOS,
                TenantId = tenantB,
                CreatedAt = new DateTime(2026, 2, 25, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 2, 27, 8, 0, 0, DateTimeKind.Utc)
            },
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = "user-a",
                Token = "aaaaaaaaaa-token-b",
                Platform = Platform.Android,
                TenantId = tenantA,
                CreatedAt = new DateTime(2026, 2, 23, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 2, 28, 8, 0, 0, DateTimeKind.Utc)
            },
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = "user-b",
                Token = "foreign-token-value",
                Platform = Platform.Android,
                TenantId = tenantForeign,
                CreatedAt = new DateTime(2026, 2, 22, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 2, 22, 9, 0, 0, DateTimeKind.Utc)
            });

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeTenantResolver(string? userId, List<Guid> tenantIds) : ITenantResolver
    {
        public string? GetCurrentUserId() => userId;

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(tenantIds);

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(tenantIds.FirstOrDefault());

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(tenantIds.Contains(tenantId));
    }
}
