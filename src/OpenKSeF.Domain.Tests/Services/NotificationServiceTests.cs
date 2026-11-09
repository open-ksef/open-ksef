using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private const string NotificationEmail = "owner@example.invalid";

    private readonly ApplicationDbContext _db;
    private readonly StubEmailService _email = new();
    private readonly ListLogger _logger = new();

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

    [Fact]
    public async Task NotifyIssuedInvoiceStuckAsync_PushesOwnerOnly_WithIssuedInvoiceData()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-user-1");
        var otherTenantId = await SeedTenantAsync("user-2");
        var provider = new StubPushProvider(PushResult.Sent);
        var invoiceId = Guid.NewGuid();

        await CreateService(provider).NotifyIssuedInvoiceStuckAsync(tenantId, invoiceId, "FV/1/11/2026");
        await CreateService(provider).NotifyIssuedInvoiceStuckAsync(otherTenantId, invoiceId, "FV/1/11/2026");

        Assert.Equal(1, provider.Calls);
        Assert.Equal("Faktura FV/1/11/2026 wymaga sprawdzenia, szczegóły w portalu. Nie wystawiaj jej ponownie bez sprawdzenia.", provider.Last!.Body);
        Assert.Equal("issuedInvoiceStuck", provider.Last.Data!["type"]);
        Assert.Equal(invoiceId.ToString(), provider.Last.Data["issuedInvoiceId"]);
        Assert.False(provider.Last.Data.ContainsKey("invoiceId"));
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_SendsEmail_WhenOwnerHasNoDevice()
    {
        var tenantId = await SeedTenantAsync("user-1", NotificationEmail);
        var provider = new StubPushProvider(PushResult.Sent);
        var evt = CreateEvent(tenantId);

        await CreateService(provider).NotifyNewInvoiceAsync(evt);

        Assert.Equal(0, provider.Calls);
        Assert.Equal([(NotificationEmail, evt.InvoiceId)], _email.Sent);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_SendsOnePushAndOneEmail_WhenDeviceAndAddressSet()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid", NotificationEmail);
        var provider = new StubPushProvider(PushResult.Sent);

        await CreateService(provider).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(1, provider.Calls);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_SendsEmail_WhenEveryPushProviderFails()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid", NotificationEmail);

        await CreateService(new StubPushProvider(PushResult.Failed), new StubPushProvider(new InvalidOperationException("down")))
            .NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_DoesNotAttemptEmail_WithoutNotificationAddress()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid");

        await CreateService(new StubPushProvider(PushResult.Sent)).NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Empty(_email.Sent);
        Assert.Equal(0, _email.Attempts);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_LogsOneWarning_WhenEmailFails()
    {
        var tenantId = await SeedTenantAsync("user-1", NotificationEmail);
        _email.Failure = new TimeoutException("smtp unreachable");

        await CreateService().NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(1, _email.Attempts);
        var warning = Assert.Single(_logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task NotifyNewInvoiceAsync_AfterEmailFailure_SkipsEmailForRestOfRun_StillPushes()
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid", NotificationEmail);
        _email.Failure = new TimeoutException("smtp unreachable");
        var push = new StubPushProvider(PushResult.Sent);
        var sut = CreateService(push);

        for (var i = 0; i < 3; i++)
            await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(1, _email.Attempts);
        Assert.Equal(3, push.Calls);
        Assert.Single(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    private NotificationService CreateService(params IPushProvider[] providers) =>
        new(
            _db,
            providers,
            _email,
            _logger);

    private async Task<Guid> SeedTenantAsync(string userId, string? notificationEmail = null)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = userId,
            Nip = "1111111111",
            NotificationEmail = notificationEmail,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedDeviceAsync(string token, string? notificationEmail = null)
    {
        var tenantId = await SeedTenantAsync("user-1", notificationEmail);
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

        public PushNotification? Last { get; private set; }

        public Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
        {
            Calls++;
            Last = notification;
            return _exception is null ? Task.FromResult(_result) : Task.FromException<PushResult>(_exception);
        }
    }

    private sealed class StubEmailService : IEmailService
    {
        public List<(string To, Guid InvoiceId)> Sent { get; } = [];

        public int Attempts { get; private set; }

        public Exception? Failure { get; set; }

        public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt)
        {
            Attempts++;
            if (Failure is not null)
                return Task.FromException(Failure);
            Sent.Add((toEmail, evt.InvoiceId));
            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger : ILogger<NotificationService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
