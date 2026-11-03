using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class InvoiceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private readonly RecordingNotificationService _notifications = new();

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task UpsertInvoicesAsync_NotifiesNewInvoicesOnlyWhenRequested(bool notify, int expectedNotifications)
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);

        var newIds = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()], notify);

        Assert.Single(newIds);
        Assert.Equal(expectedNotifications, _notifications.Count);
    }

    [Fact]
    public async Task UpsertInvoicesAsync_FillsDueDate_KeepsItWithoutXml_AndNeverTouchesPaid()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var ids = await sut.UpsertInvoicesAsync(tenantId, [CreateInvoice()], notify: false);
        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        Assert.Null(stored.DueDate);
        stored.IsPaid = true;
        stored.PaidAt = paidAt;
        await _db.SaveChangesAsync();

        await sut.UpsertInvoicesAsync(tenantId, [CreateInvoice() with { DueDate = new DateOnly(2026, 11, 20) }], notify: false);
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);

        await sut.UpsertInvoicesAsync(tenantId, [CreateInvoice()], notify: false);
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);
        Assert.True(stored.IsPaid);
        Assert.Equal(paidAt, stored.PaidAt);
    }

    [Fact]
    public async Task UpsertInvoicesAsync_NewInvoice_StoresDueDate()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);

        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice() with { DueDate = new DateOnly(2026, 11, 20) }], notify: false);

        Assert.Equal(new DateOnly(2026, 11, 20), (await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0])).DueDate);
    }

    [Theory]
    [InlineData("2026-11-03", "2026-11-03T22:59:00Z", false, false)]
    [InlineData("2026-11-03", "2026-11-03T23:00:00Z", false, true)]
    [InlineData("2026-07-03", "2026-07-03T21:59:00Z", false, false)]
    [InlineData("2026-07-03", "2026-07-03T22:00:00Z", false, true)]
    [InlineData("2026-11-04", "2026-11-03T12:00:00Z", false, false)]
    [InlineData("2026-11-02", "2026-11-03T12:00:00Z", false, true)]
    [InlineData("2026-11-02", "2026-11-03T12:00:00Z", true, false)]
    [InlineData(null, "2026-11-03T12:00:00Z", false, false)]
    public void IsOverdue_UsesTodayInWarsaw_NotPaidNotUndated(string? due, string now, bool isPaid, bool expected)
    {
        var dueDate = due is null ? (DateOnly?)null : DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, Entities.InvoiceHeader.IsOverdue(isPaid, dueDate, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static InvoiceDto CreateInvoice() => new(
        Number: "KSEF-1",
        ReferenceNumber: "REF-1",
        InvoiceNumber: "FV/1",
        VendorName: "Vendor",
        VendorNip: "1234567890",
        BuyerName: null,
        BuyerNip: null,
        AmountNet: 100m,
        AmountVat: 23m,
        AmountGross: 123m,
        Currency: "PLN",
        IssueDate: DateTime.UtcNow,
        AcquisitionDate: DateTime.UtcNow,
        InvoiceType: "VAT");

    public void Dispose() => _db.Dispose();

    private sealed class RecordingNotificationService : INotificationService
    {
        public int Count { get; private set; }

        public Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt)
        {
            Count++;
            return Task.CompletedTask;
        }

        public Task NotifyIssuedInvoiceStuckAsync(Guid tenantId, Guid issuedInvoiceId, string? number) => Task.CompletedTask;

        public Task SendConfirmationAsync(string deviceToken, Platform platform) => Task.CompletedTask;

        public Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform) => Task.FromResult(true);
    }
}
