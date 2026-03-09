using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class TenantResolverTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public TenantResolverTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);

        _db.Tenants.AddRange(
            new Tenant
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                UserId = "user-a",
                Nip = "1111111111",
                DisplayName = "Tenant A1",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Tenant
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                UserId = "user-a",
                Nip = "2222222222",
                DisplayName = "Tenant A2",
                CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Tenant
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                UserId = "user-b",
                Nip = "3333333333",
                DisplayName = "Tenant B1",
                CreatedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetUserTenantIdsAsync_ReturnsOnlyCurrentUserTenants()
    {
        var resolver = CreateResolver("user-a");

        var tenantIds = await resolver.GetUserTenantIdsAsync();

        Assert.Equal(2, tenantIds.Count);
        Assert.Contains(Guid.Parse("11111111-1111-1111-1111-111111111111"), tenantIds);
        Assert.Contains(Guid.Parse("22222222-2222-2222-2222-222222222222"), tenantIds);
        Assert.DoesNotContain(Guid.Parse("33333333-3333-3333-3333-333333333333"), tenantIds);
    }

    [Fact]
    public async Task GetCurrentTenantIdAsync_ReturnsOldestTenantForUser()
    {
        var resolver = CreateResolver("user-a");

        var tenantId = await resolver.GetCurrentTenantIdAsync();

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), tenantId);
    }

    [Fact]
    public async Task GetUserTenantIdsAsync_MissingUserClaim_ReturnsEmpty()
    {
        var resolver = CreateResolver();

        var tenantIds = await resolver.GetUserTenantIdsAsync();

        Assert.Empty(tenantIds);
    }

    [Fact]
    public async Task HasAccessToTenantAsync_ReturnsTrueOnlyForOwnedTenant()
    {
        var resolver = CreateResolver("user-a");

        var hasOwned = await resolver.HasAccessToTenantAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var hasForeign = await resolver.HasAccessToTenantAsync(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Assert.True(hasOwned);
        Assert.False(hasForeign);
    }

    private TenantResolver CreateResolver(string? userId = null)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", userId)
            ], "test"));
        }

        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        return new TenantResolver(accessor, _db, NullLogger<TenantResolver>.Instance);
    }

    public void Dispose() => _db.Dispose();
}
