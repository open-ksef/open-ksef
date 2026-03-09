using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Controllers;

public class InvoicesSummaryControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly string _userId = "user-1";
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    public InvoicesSummaryControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);

        _db.Tenants.AddRange(
            new Tenant
            {
                Id = _tenantAId,
                UserId = _userId,
                Nip = "1234567890",
                DisplayName = "Tenant A",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = _tenantBId,
                UserId = _userId,
                Nip = "2234567890",
                DisplayName = "Tenant B",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = _otherTenantId,
                UserId = "other-user",
                Nip = "3234567890",
                DisplayName = "Other Tenant",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        _db.SaveChanges();
    }

    private InvoicesSummaryController CreateController() =>
        new(_db, _currentUser);

    private static InvoiceHeader MakeInvoice(Guid tenantId, string number, DateTime issueDate) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = $"{number}-ref",
            VendorName = "Vendor",
            VendorNip = "9876543210",
            AmountGross = 100m,
            Currency = "PLN",
            IssueDate = issueDate,
            FirstSeenAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task List_WithoutTenantFilter_ReturnsOnlyCurrentUserTenantInvoices()
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice(_tenantAId, "A-001", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice(_tenantBId, "B-001", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice(_otherTenantId, "C-001", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List() as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Equal(2, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
        Assert.DoesNotContain(paged.Items, i => i.KSeFInvoiceNumber == "C-001");
    }

    [Fact]
    public async Task List_WithTenantFilterForOtherUserTenant_ReturnsForbid()
    {
        var controller = CreateController();
        var result = await controller.List(tenantId: _otherTenantId);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task List_WithTenantFilter_ReturnsOnlySelectedTenantInvoices()
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice(_tenantAId, "A-001", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice(_tenantBId, "B-001", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(tenantId: _tenantBId) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Single(paged.Items);
        Assert.Equal("B-001", paged.Items[0].KSeFInvoiceNumber);
    }

    [Fact]
    public async Task List_WithDateRangeAndPagination_AppliesFiltersAndOrdering()
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice(_tenantAId, "A-001", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice(_tenantAId, "A-002", new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice(_tenantAId, "A-003", new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(
            dateFrom: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            dateTo: new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            page: 1,
            pageSize: 1) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Equal(2, paged.TotalCount);
        Assert.Single(paged.Items);
        Assert.Equal("A-003", paged.Items[0].KSeFInvoiceNumber);
    }

    [Fact]
    public async Task List_WithPageSizeAboveLimit_CapsTo100()
    {
        for (var i = 0; i < 105; i++)
        {
            _db.InvoiceHeaders.Add(MakeInvoice(_tenantAId, $"A-{i:D3}", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i)));
        }

        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(page: 1, pageSize: 1000) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Equal(105, paged.TotalCount);
        Assert.Equal(100, paged.PageSize);
        Assert.Equal(100, paged.Items.Count);
    }

    [Fact]
    public async Task GetByNumber_WithoutTenantId_SearchesAcrossAllOwnedTenants()
    {
        _db.InvoiceHeaders.Add(MakeInvoice(_tenantBId, "B-999", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetByNumber("B-999") as OkObjectResult;

        var invoice = Assert.IsType<InvoiceResponse>(result!.Value);
        Assert.Equal("B-999", invoice.KSeFInvoiceNumber);
    }

    [Fact]
    public async Task GetByNumber_WithTenantFilterForOtherUserTenant_ReturnsForbid()
    {
        var controller = CreateController();
        var result = await controller.GetByNumber("X-001", _otherTenantId);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetByNumber_NotFound_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.GetByNumber("MISSING");

        Assert.IsType<NotFoundResult>(result);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
