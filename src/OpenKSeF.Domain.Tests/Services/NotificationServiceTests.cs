using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_DoesNotRemoveSignalRFallbackToken_WhenRemotePushFails()
    {
        var tenantId = await SeedDeviceAsync("device-local-fallback");
        var sut = CreateService(pushSuccess: false);

        await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));

        var device = await _db.DeviceTokens.SingleAsync();
        Assert.Equal("device-local-fallback", device.Token);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_RemovesNativeToken_WhenRemotePushFails()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-invalid");
        var sut = CreateService(pushSuccess: false);

        await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Empty(await _db.DeviceTokens.ToListAsync());
    }

    private NotificationService CreateService(bool pushSuccess) =>
        new(
            _db,
            [new StubPushProvider(pushSuccess)],
            new StubEmailService(),
            NullLogger<NotificationService>.Instance);

    private async Task<Guid> SeedDeviceAsync(string token)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = "user-1",
            Nip = "1234567890",
            CreatedAt = now,
            UpdatedAt = now
        });

        _db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Token = token,
            Platform = Platform.Android,
            TenantId = tenantId,
            CreatedAt = now,
            UpdatedAt = now
        });

        await _db.SaveChangesAsync();
        return tenantId;
    }

    private static NewInvoiceDetectedEvent CreateEvent(Guid tenantId) =>
        new(
            tenantId,
            Guid.NewGuid(),
            "Vendor",
            "FV/1",
            123.45m,
            "PLN");

    public void Dispose() => _db.Dispose();

    private sealed class StubPushProvider : IPushProvider
    {
        private readonly bool _success;

        public StubPushProvider(bool success)
        {
            _success = success;
        }

        public Task<bool> SendAsync(string deviceToken, PushNotification notification) =>
            Task.FromResult(_success);
    }

    private sealed class StubEmailService : IEmailService
    {
        public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt) =>
            Task.CompletedTask;
    }
}
