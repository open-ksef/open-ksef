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

        public Task SendConfirmationAsync(string deviceToken, Platform platform) => Task.CompletedTask;

        public Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform) => Task.FromResult(true);
    }
}
