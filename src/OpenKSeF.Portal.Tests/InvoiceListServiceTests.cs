using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Portal.Services;

namespace OpenKSeF.Portal.Tests;

public class InvoiceListServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public InvoiceListServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SearchAsync_WithoutTenantFilter_ReturnsOnlyAllowedTenants()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedInvoices(tenantA, tenantB, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA, tenantB]);
        var service = new InvoiceListService(_db, resolver);

        var result = await service.SearchAsync(new InvoiceListQuery
        {
            Page = 1,
            PageSize = 50
        });

        Assert.Equal(4, result.TotalCount);
        Assert.DoesNotContain(result.Items, item => item.TenantId == tenantForeign);
    }

    [Fact]
    public async Task SearchAsync_WithTenantAndDateFilters_ReturnsFilteredRows()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedInvoices(tenantA, tenantB, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA, tenantB]);
        var service = new InvoiceListService(_db, resolver);

        var result = await service.SearchAsync(new InvoiceListQuery
        {
            Page = 1,
            PageSize = 50,
            TenantId = tenantA,
            DateFrom = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal(1, result.TotalCount);
        var row = Assert.Single(result.Items);
        Assert.Equal(tenantA, row.TenantId);
        Assert.Equal(new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc), row.IssueDate.Date);
    }

    [Fact]
    public async Task SearchAsync_AppliesSortingAndPaging()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenantForeign = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        SeedInvoices(tenantA, tenantB, tenantForeign);

        var resolver = new FakeTenantResolver([tenantA, tenantB]);
        var service = new InvoiceListService(_db, resolver);

        var result = await service.SearchAsync(new InvoiceListQuery
        {
            Page = 2,
            PageSize = 2,
            SortBy = InvoiceSortBy.AmountGross,
            SortDirection = SortDirection.Desc
        });

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].AmountGross >= result.Items[1].AmountGross);
    }

    private void SeedInvoices(Guid tenantA, Guid tenantB, Guid tenantForeign)
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice(tenantA, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100m, "A-1"),
            MakeInvoice(tenantA, new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc), 300m, "A-2"),
            MakeInvoice(tenantB, new DateTime(2026, 2, 12, 0, 0, 0, DateTimeKind.Utc), 200m, "B-1"),
            MakeInvoice(tenantB, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), 400m, "B-2"),
            MakeInvoice(tenantForeign, new DateTime(2026, 2, 8, 0, 0, 0, DateTimeKind.Utc), 999m, "C-1"));

        _db.SaveChanges();
    }

    private static InvoiceHeader MakeInvoice(Guid tenantId, DateTime issueDate, decimal amount, string number) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = number + "-ref",
            VendorName = "Vendor",
            VendorNip = "9876543210",
            AmountGross = amount,
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

        public Task<Guid?> GetCurrentTenantIdAsync() => Task.FromResult<Guid?>(tenantIds.FirstOrDefault());

        public Task<bool> HasAccessToTenantAsync(Guid tenantId) => Task.FromResult(tenantIds.Contains(tenantId));
    }
}
