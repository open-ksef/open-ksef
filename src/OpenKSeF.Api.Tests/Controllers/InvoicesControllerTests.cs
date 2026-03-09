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

public class InvoicesControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransferDetailsService _transferDetails;
    private readonly IQrCodeService _qrCode;
    private readonly string _userId = "user-1";
    private readonly Guid _tenantId = Guid.NewGuid();

    public InvoicesControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);

        _transferDetails = new TransferDetailsService();
        _qrCode = Substitute.For<IQrCodeService>();
        _qrCode.GenerateTransferQr(Arg.Any<TransferData>()).Returns(new byte[] { 0x89, 0x50 });

        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = _userId,
            Nip = "1234567890",
            DisplayName = "Test Tenant",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private InvoicesController CreateController() =>
        new(_db, _currentUser, _transferDetails, _qrCode);

    private InvoiceHeader MakeInvoice(string ksefNumber, DateTime issueDate, string? vendor = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            KSeFInvoiceNumber = ksefNumber,
            KSeFReferenceNumber = ksefNumber + "-ref",
            VendorName = vendor ?? "Vendor A",
            VendorNip = "9876543210",
            AmountGross = 100m,
            Currency = "PLN",
            IssueDate = issueDate,
            FirstSeenAt = DateTime.UtcNow
        };

    [Fact]
    public async Task List_WithDateFromFilter_ReturnsInvoicesAfterDate()
    {
        var old = MakeInvoice("OLD-001", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = MakeInvoice("NEW-001", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        _db.InvoiceHeaders.AddRange(old, recent);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(_tenantId, dateFrom: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Single(paged.Items);
        Assert.Equal("NEW-001", paged.Items[0].KSeFInvoiceNumber);
    }

    [Fact]
    public async Task List_WithDateToFilter_ReturnsInvoicesBeforeDate()
    {
        var old = MakeInvoice("OLD-001", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = MakeInvoice("NEW-001", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        _db.InvoiceHeaders.AddRange(old, recent);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(_tenantId, dateTo: new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc)) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Single(paged.Items);
        Assert.Equal("OLD-001", paged.Items[0].KSeFInvoiceNumber);
    }

    [Fact]
    public async Task List_WithDateRange_ReturnsOnlyInvoicesInRange()
    {
        _db.InvoiceHeaders.AddRange(
            MakeInvoice("I-1", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice("I-2", new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)),
            MakeInvoice("I-3", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.List(
            _tenantId,
            dateFrom: new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            dateTo: new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc)) as OkObjectResult;

        var paged = Assert.IsType<PagedResult<InvoiceResponse>>(result!.Value);
        Assert.Single(paged.Items);
        Assert.Equal("I-2", paged.Items[0].KSeFInvoiceNumber);
    }

    [Fact]
    public async Task GetByKSeFNumber_ExistingInvoice_ReturnsOk()
    {
        var invoice = MakeInvoice("KSEF-12345", DateTime.UtcNow.Date);
        _db.InvoiceHeaders.Add(invoice);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetByKSeFNumber(_tenantId, "KSEF-12345") as OkObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<InvoiceResponse>(result.Value);
        Assert.Equal("KSEF-12345", response.KSeFInvoiceNumber);
    }

    [Fact]
    public async Task GetByKSeFNumber_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.GetByKSeFNumber(_tenantId, "NONEXISTENT");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetByKSeFNumber_WrongTenant_ReturnsForbid()
    {
        var otherTenantId = Guid.NewGuid();
        var controller = CreateController();
        // Other tenant doesn't belong to this user
        var result = await controller.GetByKSeFNumber(otherTenantId, "KSEF-12345");
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetTransferDetails_ExistingInvoice_ReturnsTransferData()
    {
        var invoice = MakeInvoice("KSEF-TR-001", DateTime.UtcNow.Date);
        _db.InvoiceHeaders.Add(invoice);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetTransferDetails(_tenantId, invoice.Id) as OkObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<TransferDetailsResponse>(result.Value);
        Assert.Equal("Vendor A", response.RecipientName);
        Assert.Equal("9876543210", response.RecipientNip);
        Assert.Equal(100m, response.Amount);
        Assert.Contains("data:image/png;base64,", response.QrCodeBase64);
    }

    [Fact]
    public async Task GetTransferDetails_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.GetTransferDetails(_tenantId, Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetPaid_MarksInvoiceAsPaid()
    {
        var invoice = MakeInvoice("KSEF-PAY-001", DateTime.UtcNow.Date);
        _db.InvoiceHeaders.Add(invoice);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.SetPaid(_tenantId, invoice.Id, new SetInvoicePaidRequest(true)) as OkObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<InvoiceResponse>(result.Value);
        Assert.True(response.IsPaid);
        Assert.NotNull(response.PaidAt);

        var dbInvoice = await _db.InvoiceHeaders.FindAsync(invoice.Id);
        Assert.True(dbInvoice!.IsPaid);
        Assert.NotNull(dbInvoice.PaidAt);
    }

    [Fact]
    public async Task SetPaid_UnmarksInvoiceAsPaid()
    {
        var invoice = MakeInvoice("KSEF-PAY-002", DateTime.UtcNow.Date);
        invoice.IsPaid = true;
        invoice.PaidAt = DateTime.UtcNow;
        _db.InvoiceHeaders.Add(invoice);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.SetPaid(_tenantId, invoice.Id, new SetInvoicePaidRequest(false)) as OkObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<InvoiceResponse>(result.Value);
        Assert.False(response.IsPaid);
        Assert.Null(response.PaidAt);
    }

    [Fact]
    public async Task SetPaid_NotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.SetPaid(_tenantId, Guid.NewGuid(), new SetInvoicePaidRequest(true));
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetPaid_WrongTenant_ReturnsForbid()
    {
        var controller = CreateController();
        var result = await controller.SetPaid(Guid.NewGuid(), Guid.NewGuid(), new SetInvoicePaidRequest(true));
        Assert.IsType<ForbidResult>(result);
    }

    public void Dispose() => _db.Dispose();
}
