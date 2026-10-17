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
    public async Task NotifyNewInvoiceAsync_SkipsAndKeepsSignalRFallbackToken()
    {
        var tenantId = await SeedDeviceAsync("device-local-fallback");
        var provider = new StubPushProvider(PushResult.InvalidToken);

        await CreateService(provider).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(0, provider.Calls);
        Assert.Equal("device-local-fallback", (await _db.DeviceTokens.SingleAsync()).Token);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_RemovesToken_WhenProviderRejectsIt()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-invalid");

        await CreateService(new StubPushProvider(PushResult.InvalidToken)).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Empty(await _db.DeviceTokens.ToListAsync());
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_KeepsToken_WhenDeliveryFails()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid");

        await CreateService(new StubPushProvider(PushResult.Failed)).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Single(await _db.DeviceTokens.ToListAsync());
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_KeepsToken_WhenRelayIsUnreachable()
    {
        // Production case: relay behind Cloudflare answered 523 -> HttpRequestException on every retry.
        var tenantId = await SeedDeviceAsync("fcm-token-valid");
        var relay = new StubPushProvider(new HttpRequestException("Relay transient failure: 523"));

        await CreateService(relay, new StubPushProvider(PushResult.Skipped)).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(3, relay.Calls);
        Assert.Single(await _db.DeviceTokens.ToListAsync());
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_KeepsToken_WhenOneProviderRejectsAndAnotherFails()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid");

        await CreateService(new StubPushProvider(PushResult.Failed), new StubPushProvider(PushResult.InvalidToken))
            .NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Single(await _db.DeviceTokens.ToListAsync());
    }

    [Theory]
    [InlineData(true, 1)]   // TenantId == null: subscribed to all companies
    [InlineData(false, 0)]  // TenantId of another company: explicit per-company subscription
    public async Task NotifyNewInvoiceAsync_RespectsDeviceTenantScope(bool allCompanies, int expectedCalls)
    {
        var invoiceTenantId = await SeedDeviceAsync("fcm-token-valid");
        var otherTenantId = await SeedTenantAsync("user-1");
        var device = await _db.DeviceTokens.SingleAsync();
        device.TenantId = allCompanies ? null : otherTenantId;
        await _db.SaveChangesAsync();
        var provider = new StubPushProvider(PushResult.Sent);

        await CreateService(provider).NotifyNewInvoiceAsync(CreateEvent(invoiceTenantId));

        Assert.Equal(expectedCalls, provider.Calls);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_DoesNotPushOtherUsersDevices()
    {
        await SeedDeviceAsync("fcm-token-user-1");
        var otherUserTenantId = await SeedTenantAsync("user-2");
        var provider = new StubPushProvider(PushResult.Sent);

        await CreateService(provider).NotifyNewInvoiceAsync(CreateEvent(otherUserTenantId));

        Assert.Equal(0, provider.Calls);
    }

    private NotificationService CreateService(params IPushProvider[] providers) =>
        new(
            _db,
            providers,
            new StubEmailService(),
            NullLogger<NotificationService>.Instance);

    private async Task<Guid> SeedTenantAsync(string userId)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = userId,
            Nip = "1234567890",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedDeviceAsync(string token)
    {
        var tenantId = await SeedTenantAsync("user-1");
        var now = DateTime.UtcNow;

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
        private readonly PushResult _result;
        private readonly Exception? _exception;

        public StubPushProvider(PushResult result) => _result = result;

        public StubPushProvider(Exception exception) => _exception = exception;

        public int Calls { get; private set; }

        public Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
        {
            Calls++;
            return _exception is null ? Task.FromResult(_result) : Task.FromException<PushResult>(_exception);
        }
    }

    private sealed class StubEmailService : IEmailService
    {
        public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt) =>
            Task.CompletedTask;
    }
}
