using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class InvoiceDetailServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public InvoiceDetailServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetByKsefNumberAsync_ReturnsInvoiceForOwnedTenant()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantForeign = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedInvoices(tenantA, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA]);
        var service = new InvoiceDetailService(_db, resolver);

        var result = await service.GetByKsefNumberAsync("KSEF-OWNED");

        Assert.NotNull(result);
        Assert.Equal("KSEF-OWNED", result.KSeFInvoiceNumber);
        Assert.Equal("Vendor A", result.VendorName);
    }

    [Fact]
    public async Task GetByKsefNumberAsync_ReturnsNullForForeignTenant()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantForeign = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedInvoices(tenantA, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA]);
        var service = new InvoiceDetailService(_db, resolver);

        var result = await service.GetByKsefNumberAsync("KSEF-FOREIGN");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByKsefNumberAsync_ReturnsNullWhenMissing()
    {
        var resolver = new FakeTenantResolver([]);
        var service = new InvoiceDetailService(_db, resolver);

        var result = await service.GetByKsefNumberAsync("NOPE");

        Assert.Null(result);
    }

    private void SeedInvoices(Guid tenantA, Guid tenantForeign)
    {
        _db.InvoiceHeaders.AddRange(
            new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                KSeFInvoiceNumber = "KSEF-OWNED",
                KSeFReferenceNumber = "REF-1",
                VendorName = "Vendor A",
                VendorNip = "1111111111",
                AmountGross = 123.45m,
                Currency = "PLN",
                IssueDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
                FirstSeenAt = new DateTime(2026, 2, 21, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc)
            },
            new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = tenantForeign,
                KSeFInvoiceNumber = "KSEF-FOREIGN",
                KSeFReferenceNumber = "REF-2",
                VendorName = "Vendor B",
                VendorNip = "2222222222",
                AmountGross = 543.21m,
                Currency = "PLN",
                IssueDate = new DateTime(2026, 2, 18, 0, 0, 0, DateTimeKind.Utc),
                FirstSeenAt = new DateTime(2026, 2, 19, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)
            });

        _db.SaveChanges();
    }

    private sealed class FakeTenantResolver(List<Guid> tenantIds) : ITenantResolver
    {
        public string? GetCurrentUserId() => "user-a";

        public Task<List<Guid>> GetUserTenantIdsAsync() => Task.FromResult(tenantIds);

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(tenantIds.FirstOrDefault());

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(tenantIds.Contains(tenantId));
    }

    public void Dispose() => _db.Dispose();
}
