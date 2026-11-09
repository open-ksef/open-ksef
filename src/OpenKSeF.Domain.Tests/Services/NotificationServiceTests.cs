using System.Globalization;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
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

    [Theory]
    [InlineData("parse", 2)]
    [InlineData("recipient", 2)]
    [InlineData("auth", 1)]
    [InlineData("connect", 1)]
    public async Task NotifyNewInvoiceAsync_SkipsLaterEmails_OnlyAfterHostLevelFailure(string failure, int expectedAttempts)
    {
        var tenantId = await SeedTenantAsync("user-1", NotificationEmail);
        _email.Failure = failure switch
        {
            "parse" => new ParseException("bad address", 0, 0),
            "recipient" => new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, new MailboxAddress("", NotificationEmail), "rejected"),
            "auth" => new AuthenticationException("bad credentials"),
            _ => new SocketException()
        };
        var sut = CreateService();

        await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));
        await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));

        Assert.Equal(expectedAttempts, _email.Attempts);
    }

    [Theory]
    [InlineData("Biuro Testowe", "Biuro Testowe")]
    [InlineData(null, "NIP 1111111111")]
    [InlineData(" ", "NIP 1111111111")]
    public async Task NotifyNewInvoicesSummaryAsync_SendsPolishSummaryPushAndEmail(string? displayName, string company)
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid", NotificationEmail, displayName);
        var provider = new StubPushProvider(PushResult.Sent);

        await CreateService(provider).NotifyNewInvoicesSummaryAsync(tenantId, 15);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("Więcej nowych faktur", provider.Last!.Title);
        Assert.Equal($"{company}: pozostałe nowe faktury: 15. Zobacz je w aplikacji.", provider.Last.Body);
        Assert.Equal(new Dictionary<string, string>
        {
            ["type"] = "newInvoicesSummary",
            ["tenantId"] = tenantId.ToString(),
            ["count"] = "15"
        }, provider.Last.Data);
        Assert.Equal([(NotificationEmail, tenantId, company, 15)], _email.Summaries);
    }

    [Theory]
    [InlineData("Dostawca Testowy", 1234567.891, "EUR", "Dostawca Testowy: 1 234 567,89 EUR")]
    [InlineData("", 0.5, "PLN", "Nieznany sprzedawca: 0,50 PLN")]
    [InlineData("  ", 999.999, "PLN", "Nieznany sprzedawca: 1 000,00 PLN")]
    [InlineData(" ACME\r\n  Sp. z o.o.\t\n", 1, "PLN", "ACME Sp. z o.o.: 1,00 PLN")]
    public async Task NotifyNewInvoiceAsync_SendsPolishPush_SameOnSignalRAndDevice(string vendor, double amount, string currency, string body)
    {
        var tenantId = await SeedDeviceAsync("fcm-token-valid");
        var provider = new StubPushProvider(PushResult.Sent);
        var signalR = new StubUserPushProvider();
        var evt = new NewInvoiceDetectedEvent(tenantId, Guid.NewGuid(), vendor, "FV/1", (decimal)amount, currency);

        await new NotificationService(_db, [provider], _email, _logger, signalR).NotifyNewInvoiceAsync(evt);

        Assert.Equal("Nowa faktura", provider.Last!.Title);
        Assert.Equal(body, provider.Last.Body);
        Assert.Equal(new Dictionary<string, string>
        {
            ["tenantId"] = tenantId.ToString(),
            ["invoiceId"] = evt.InvoiceId.ToString()
        }, provider.Last.Data);
        Assert.Same(provider.Last, signalR.Last);
    }

    [Fact]
    public void EnsurePolishFormatting_PassesForPolish_ThrowsForInvariant()
    {
        NotificationService.EnsurePolishFormatting(CultureInfo.GetCultureInfo("pl-PL"));

        var ex = Assert.Throws<InvalidOperationException>(() => NotificationService.EnsurePolishFormatting(CultureInfo.InvariantCulture));
        Assert.Contains("1,234.50", ex.Message);
    }

    [Fact]
    public async Task NotifyNewInvoicesSummaryAsync_SendsEmail_WithoutDevice_AndPush_WithoutAddress()
    {
        var noDevice = await SeedTenantAsync("user-2", NotificationEmail);
        var noAddress = await SeedDeviceAsync("fcm-token-valid");
        var provider = new StubPushProvider(PushResult.Sent);
        var sut = CreateService(provider);

        await sut.NotifyNewInvoicesSummaryAsync(noDevice, 3);
        await sut.NotifyNewInvoicesSummaryAsync(noAddress, 4);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("4", provider.Last!.Data!["count"]);
        Assert.Equal([(NotificationEmail, noDevice, "NIP 1111111111", 3)], _email.Summaries);
    }

    [Fact]
    public async Task NotifyNewInvoicesSummaryAsync_SkipsEmail_AfterHostFailureInRun()
    {
        var tenantId = await SeedTenantAsync("user-1", NotificationEmail);
        _email.Failure = new SocketException();
        var sut = CreateService();

        await sut.NotifyNewInvoiceAsync(CreateEvent(tenantId));
        await sut.NotifyNewInvoicesSummaryAsync(tenantId, 5);

        Assert.Equal(1, _email.Attempts);
    }

    private NotificationService CreateService(params IPushProvider[] providers) =>
        new(
            _db,
            providers,
            _email,
            _logger);

    private async Task<Guid> SeedTenantAsync(string userId, string? notificationEmail = null, string? displayName = null)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = userId,
            Nip = "1111111111",
            DisplayName = displayName,
            NotificationEmail = notificationEmail,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedDeviceAsync(string token, string? notificationEmail = null, string? displayName = null)
    {
        var tenantId = await SeedTenantAsync("user-1", notificationEmail, displayName);
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

    private sealed class StubUserPushProvider : IUserPushProvider
    {
        public PushNotification? Last { get; private set; }

        public Task<bool> SendToUserAsync(string userId, PushNotification notification)
        {
            Last = notification;
            return Task.FromResult(true);
        }
    }

    private sealed class StubEmailService : IEmailService
    {
        public List<(string To, Guid InvoiceId)> Sent { get; } = [];

        public int Attempts { get; private set; }

        public Exception? Failure { get; set; }

        public List<(string To, Guid TenantId, string Company, int Count)> Summaries { get; } = [];

        public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt)
        {
            Attempts++;
            if (Failure is not null)
                return Task.FromException(Failure);
            Sent.Add((toEmail, evt.InvoiceId));
            return Task.CompletedTask;
        }

        public Task SendNewInvoicesSummaryEmailAsync(string toEmail, Guid tenantId, string company, int count)
        {
            Attempts++;
            if (Failure is not null)
                return Task.FromException(Failure);
            Summaries.Add((toEmail, tenantId, company, count));
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
