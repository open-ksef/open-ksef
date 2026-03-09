using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class TenantCrudServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public TenantCrudServiceTests()
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
                DisplayName = "Tenant A",
                NotificationEmail = "a@example.com",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                UserId = "user-b",
                Nip = "2222222222",
                DisplayName = "Tenant B",
                NotificationEmail = "b@example.com",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        _db.SaveChanges();
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCurrentUserTenants()
    {
        var resolver = new FakeTenantResolver("user-a", true);
        var service = new TenantCrudService(_db, resolver);

        var tenants = await service.ListAsync();

        var tenant = Assert.Single(tenants);
        Assert.Equal("1111111111", tenant.Nip);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNipForUser_ReturnsFailure()
    {
        var resolver = new FakeTenantResolver("user-a", true);
        var service = new TenantCrudService(_db, resolver);

        var result = await service.CreateAsync(new TenantFormModel
        {
            Nip = "1111111111",
            DisplayName = "Duplicate",
            NotificationEmail = "dup@example.com"
        });

        Assert.False(result.Success);
        Assert.Equal("A tenant with this NIP already exists.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_ForeignTenant_ReturnsFailure()
    {
        var resolver = new FakeTenantResolver("user-a", false);
        var service = new TenantCrudService(_db, resolver);

        var result = await service.UpdateAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new TenantFormModel
            {
                Nip = "2222222222",
                DisplayName = "Tenant B Updated"
            });

        Assert.False(result.Success);
        Assert.Equal("You do not have access to this tenant.", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAsync_OwnedTenant_RemovesRecord()
    {
        var resolver = new FakeTenantResolver("user-a", true);
        var service = new TenantCrudService(_db, resolver);

        var result = await service.DeleteAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.True(result.Success);
        Assert.False(await _db.Tenants.AnyAsync(t => t.Id == Guid.Parse("11111111-1111-1111-1111-111111111111")));
    }

    private sealed class FakeTenantResolver(string? userId, bool hasAccess) : ITenantResolver
    {
        public string? GetCurrentUserId() => userId;

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(new List<Guid>());

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(null);

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(hasAccess);
    }

    public void Dispose() => _db.Dispose();
}
