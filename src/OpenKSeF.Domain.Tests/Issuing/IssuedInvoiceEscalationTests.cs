using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Issuing;

public class IssuedInvoiceEscalationTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromHours(24);
    private static readonly DateTime IssuedAt = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly MovableTime _time = new(IssuedAt);
    private readonly RecordingNotifications _notifications = new();
    private readonly ListLogger _logs = new();

    private ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<Guid> SeedAsync(Action<IssuedInvoice>? arrange = null)
    {
        await using var db = NewDb();
        var invoice = Fa3InvoiceWriterTests.Issued(i => i.AddLine("Usługa", null, 1, 100m, VatRate.Rate23), now: IssuedAt);
        invoice.AttachSentXml([1], "hash", "test", null);
        arrange?.Invoke(invoice);
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task RunAsync()
    {
        await using var db = NewDb();
        await new IssuedInvoiceEscalationService(db, _notifications, _time, _logs).EscalateAsync(Threshold, CancellationToken.None);
    }

    private async Task<IssuedInvoice> LoadAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.IssuedInvoices.SingleAsync(i => i.Id == id);
    }

    private async Task MutateAsync(Guid id, Action<IssuedInvoice> change)
    {
        await using var db = NewDb();
        change(await db.IssuedInvoices.SingleAsync(i => i.Id == id));
        await db.SaveChangesAsync();
    }

    private static void Sending(IssuedInvoice invoice)
    {
        invoice.MarkSendStarted("sess-1", IssuedAt);
        invoice.RecordElementRef("el-1", IssuedAt);
    }

    private static void AcceptedWithoutUpo(IssuedInvoice invoice)
    {
        Sending(invoice);
        invoice.MarkAccepted("KSEF-1", IssuedAt, 200, "Sukces", IssuedAt);
    }

    [Theory]
    [InlineData(IssuedInvoiceStatus.Queued)]
    [InlineData(IssuedInvoiceStatus.Sending)]
    [InlineData(IssuedInvoiceStatus.Accepted)]
    public async Task PastThreshold_ErrorOncePerWindow_OneNotification_StatusUnchanged(IssuedInvoiceStatus status)
    {
        var id = await SeedAsync(status switch
        {
            IssuedInvoiceStatus.Sending => Sending,
            IssuedInvoiceStatus.Accepted => AcceptedWithoutUpo,
            _ => null
        });

        _time.Advance(Threshold);
        for (var cycle = 0; cycle < 100; cycle++)
        {
            await RunAsync();
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Single(_logs.Errors);
        Assert.Single(_notifications.Calls);
        var flagged = await LoadAsync(id);
        Assert.Equal(status, flagged.Status);
        Assert.NotNull(flagged.EscalatedAt);

        _time.Advance(Threshold);
        await RunAsync();
        await RunAsync();

        Assert.Equal(2, _logs.Errors.Count);
        Assert.Single(_notifications.Calls);
        Assert.Equal(status, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task BeforeThreshold_NothingHappens()
    {
        var id = await SeedAsync(Sending);
        _time.Advance(Threshold - TimeSpan.FromSeconds(1));

        await RunAsync();

        Assert.Empty(_logs.Errors);
        Assert.Empty(_notifications.Calls);
        Assert.Null((await LoadAsync(id)).EscalatedAt);
    }

    [Fact]
    public async Task ResendAfterThreshold_ClockNotRestarted_NoSecondNotification()
    {
        var id = await SeedAsync(Sending);
        _time.Advance(Threshold);
        await RunAsync();

        await MutateAsync(id, invoice =>
        {
            invoice.ScheduleResend("x", _time.UtcNow, _time.UtcNow);
            invoice.MarkSendStarted("sess-2", _time.UtcNow);
        });
        _time.Advance(Threshold);
        await RunAsync();

        Assert.Equal(2, _logs.Errors.Count);
        Assert.Single(_notifications.Calls);
    }

    [Fact]
    public async Task UpoArrives_FlagCleared_NoFurtherEscalation()
    {
        var id = await SeedAsync(AcceptedWithoutUpo);
        _time.Advance(Threshold);
        await RunAsync();

        await MutateAsync(id, invoice => invoice.AttachUpo(Encoding.UTF8.GetBytes("<UPO/>"), _time.UtcNow));
        _time.Advance(Threshold);
        await RunAsync();

        Assert.Null((await LoadAsync(id)).EscalatedAt);
        Assert.Single(_logs.Errors);
    }

    [Fact]
    public async Task Rejected_FlagCleared_NoFurtherEscalation()
    {
        var id = await SeedAsync(Sending);
        _time.Advance(Threshold);
        await RunAsync();

        await MutateAsync(id, invoice => invoice.MarkRejected(450, "Błąd", null, _time.UtcNow));
        _time.Advance(Threshold);
        await RunAsync();

        Assert.Null((await LoadAsync(id)).EscalatedAt);
        Assert.Single(_logs.Errors);
    }

    [Fact]
    public async Task Log_HasRefsAndLastError_NoSecrets()
    {
        await SeedAsync(i => { Sending(i); i.ScheduleRetry("KSeF nie odpowiada", IssuedAt, IssuedAt); });
        _time.Advance(Threshold);

        await RunAsync();

        var line = Assert.Single(_logs.Errors);
        Assert.Contains("KSeF nie odpowiada", line);
        Assert.Contains("sess-1", line);
        Assert.DoesNotContain("oken", line);
    }

    private sealed class MovableTime(DateTime now) : TimeProvider
    {
        private DateTimeOffset _now = new(now);
        public DateTime UtcNow => _now.UtcDateTime;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(Guid TenantId, Guid InvoiceId, string? Number)> Calls { get; } = [];

        public Task NotifyIssuedInvoiceStuckAsync(Guid tenantId, Guid issuedInvoiceId, string? number)
        {
            Calls.Add((tenantId, issuedInvoiceId, number));
            return Task.CompletedTask;
        }

        public Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt) => Task.CompletedTask;
        public Task NotifyNewInvoicesSummaryAsync(Guid tenantId, int count) => Task.CompletedTask;
        public Task SendConfirmationAsync(string deviceToken, Platform platform) => Task.CompletedTask;
        public Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform) => Task.FromResult(true);
    }

    private sealed class ListLogger : ILogger<IssuedInvoiceEscalationService>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
