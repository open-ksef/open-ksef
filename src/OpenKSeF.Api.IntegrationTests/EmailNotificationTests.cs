using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;
using OpenKSeF.Domain.Tests;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

internal static class Mailpit
{
    public const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    public static IContainer Create(int? hostSmtpPort = null, params (string Name, string Value)[] environment)
    {
        var builder = new ContainerBuilder("axllent/mailpit:v1.30.0")
            .WithPortBinding(HttpPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/api/v1/info")));
        builder = hostSmtpPort is null ? builder.WithPortBinding(SmtpPort, true) : builder.WithPortBinding(hostSmtpPort.Value, SmtpPort);
        foreach (var (name, value) in environment)
            builder = builder.WithEnvironment(name, value);
        return builder.Build();
    }

    public static async Task<List<(string Subject, string To)>> MessagesAsync(IContainer mailpit)
    {
        using var http = new HttpClient();
        var body = await http.GetFromJsonAsync<JsonElement>(
            $"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(HttpPort)}/api/v1/messages");
        return body.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("Subject").GetString()!, m.GetProperty("To")[0].GetProperty("Address").GetString()!))
            .ToList();
    }

    public static async Task<string> SnippetAsync(IContainer mailpit, string subject)
    {
        using var http = new HttpClient();
        var body = await http.GetFromJsonAsync<JsonElement>(
            $"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(HttpPort)}/api/v1/messages");
        return body.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("Subject").GetString() == subject)
            .GetProperty("Snippet").GetString()!;
    }
}

public sealed class EmailNotificationTests : IAsyncLifetime
{
    private const string NipA = "1111111111";
    private const string NipB = "1111111112";
    private const string AddressA = "a@example.invalid";
    private const string AddressB = "b@example.invalid";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IContainer _mailpit = Mailpit.Create();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly FixedTime _time = new(DateTimeOffset.UtcNow);

    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _received = _ => Page([]);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mailpit.StartAsync());
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                return criteria.Direction == InvoiceDirection.Received ? _received(criteria) : Page([], criteria.DateTo);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<byte>());
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _mailpit.DisposeAsync();
    }

    [Fact]
    public async Task IncrementalSync_EmailsNewInvoice_FirstSyncAndFullResyncDoNot()
    {
        _received = c => c.PageOffset > 0 ? Page([])
            : c.SubjectNip == NipA ? Page([Bought("A-1")])
            : Page([Bought("B-1")]);

        await using (var db = NewContext())
            Assert.All(await SyncService(db, MailpitOptions()).SyncAllTenantsAsync(), r => Assert.Equal(TenantSyncOutcome.Success, r.Outcome));

        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1"), Bought("A-2")]) : Page([]);
        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions()).SyncTenantAsync(_tenantA, forceFullResync: true)).Outcome);

        Assert.Equal([("Nowa faktura od Dostawca", AddressA)], await Mailpit.MessagesAsync(_mailpit));
    }

    [Fact]
    public async Task IncrementalSync_TwoNewInvoices_SendsExactlyTwoEmails()
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1"), Bought("A-2")]) : Page([]);

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions()).SyncTenantAsync(_tenantA)).Outcome);
        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions()).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(
            [("Nowa faktura od Dostawca", AddressA), ("Nowa faktura od Dostawca", AddressA)],
            await Mailpit.MessagesAsync(_mailpit));
    }

    [Fact]
    public async Task EmailDisabledByDefault_NoConnection_SyncSucceeds()
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1")]) : Page([]);
        var options = new EmailOptions { SmtpHost = _mailpit.Hostname, SmtpPort = _mailpit.GetMappedPublicPort(Mailpit.SmtpPort), UseSsl = false };

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, options).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Empty(await Mailpit.MessagesAsync(_mailpit));
        await using var check = NewContext();
        Assert.True(await check.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "A-1"));
    }

    [Fact]
    public async Task UnreachableSmtp_InvoiceStored_SyncSucceeds()
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1")]) : Page([]);
        var options = new EmailOptions { Enabled = true, SmtpHost = "127.0.0.1", SmtpPort = 9, UseSsl = false };

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, options).SyncTenantAsync(_tenantA)).Outcome);

        await using var check = NewContext();
        Assert.True(await check.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "A-1"));
    }

    [Fact]
    public async Task SameRun_BadAddressOnTenantA_StillEmailsTenantB()
    {
        _received = c => c.PageOffset > 0 ? Page([])
            : c.SubjectNip == NipA ? Page([Bought("A-1")])
            : Page([Bought("B-1")]);
        var lastSync = DateTime.UtcNow.AddHours(-4);
        await using (var db = NewContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Tenants" SET "NotificationEmail" = 'not an address' WHERE "Id" = {_tenantA};
                INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync")
                VALUES ({Guid.NewGuid()}, {_tenantB}, {lastSync}, {lastSync})
                """);

        await using (var db = NewContext())
        {
            var sut = SyncService(db, MailpitOptions());
            Assert.Equal(TenantSyncOutcome.Success, (await sut.SyncTenantAsync(_tenantA)).Outcome);
            Assert.Equal(TenantSyncOutcome.Success, (await sut.SyncTenantAsync(_tenantB)).Outcome);
        }

        Assert.Equal([("Nowa faktura od Dostawca", AddressB)], await Mailpit.MessagesAsync(_mailpit));
    }

    [Fact]
    public async Task DeadSmtpHost_ThreeNewInvoices_OneWarning_RestSkippedAtInformation_AllStored_NoAddressInLogs()
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1"), Bought("A-2"), Bought("A-3")]) : Page([]);
        var options = new EmailOptions { Enabled = true, SmtpHost = "127.0.0.1", SmtpPort = 9, UseSsl = false };
        var log = new List<(LogLevel Level, string Category, string Text)>();

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, options, log).SyncTenantAsync(_tenantA)).Outcome);

        var notify = log.Where(e => e.Category == nameof(NotificationService)).ToList();
        Assert.Single(notify, e => e.Level == LogLevel.Warning);
        Assert.Equal(2, notify.Count(e => e.Level == LogLevel.Information && e.Text.Contains("Email skipped")));
        Assert.DoesNotContain(log, e => e.Text.Contains(AddressA));
        await using var check = NewContext();
        Assert.Equal(["A-1", "A-2", "A-3"], await check.InvoiceHeaders.Where(i => i.TenantId == _tenantA).Select(i => i.KSeFInvoiceNumber).OrderBy(n => n).ToListAsync());
    }

    [Fact]
    public async Task IncrementalSync_AfterThirtyDayGap_EmailsEveryNewInvoice()
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1"), Bought("A-2")]) : Page([]);
        var lastSync = DateTime.UtcNow.AddDays(-30);
        await using (var db = NewContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "SyncStates" SET "LastSyncedAt" = {lastSync}, "LastSuccessfulSync" = {lastSync} WHERE "TenantId" = {_tenantA}
                """);

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions()).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(2, (await Mailpit.MessagesAsync(_mailpit)).Count(m => m.To == AddressA));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public async Task IncrementalSync_NotifiesNewestTen_OneSummaryForRest_StoresAll(int count)
    {
        var acquiredAt = DateTime.UtcNow.AddHours(-2);
        var invoices = Enumerable.Range(0, count).Select(i => Bought($"A-{i:D2}", acquiredAt.AddMinutes(i))).ToList();
        _received = c => c.SubjectNip == NipA ? Page(invoices.Skip(c.PageOffset * 8).Take(8).ToList()) : Page([]);
        var push = new RecordingPushProvider();
        await using (var db = NewContext())
        {
            db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = $"user-{NipA}", Token = "fcm-token-a", Platform = Platform.Android, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions(), push: push, batchSize: 8).SyncTenantAsync(_tenantA)).Outcome);

        await using var check = NewContext();
        var stored = await check.InvoiceHeaders.Where(i => i.TenantId == _tenantA).ToListAsync();
        Assert.Equal(invoices.Select(i => i.KSeFNumber).Order(), stored.Select(i => i.KSeFInvoiceNumber).Order());
        Assert.All(stored, i => Assert.False(i.IsPaid));
        Assert.All(stored, i => Assert.Null(i.PaidAt));
        var newestTen = stored.OrderByDescending(i => i.AcquisitionDate).Take(10).Select(i => i.Id.ToString()).Order();
        Assert.Equal(newestTen, push.Sent.Where(n => n.Data!.ContainsKey("invoiceId")).Select(n => n.Data!["invoiceId"]).Order());
        var messages = await Mailpit.MessagesAsync(_mailpit);
        Assert.Equal(10, messages.Count(m => m == ("Nowa faktura od Dostawca", AddressA)));
        if (count == 10)
        {
            Assert.Equal(10, push.Sent.Count);
            Assert.Equal(10, messages.Count);
            return;
        }

        var summary = Assert.Single(push.Sent, n => !n.Data!.ContainsKey("invoiceId"));
        Assert.Equal(11, push.Sent.Count);
        Assert.Equal("Więcej nowych faktur", summary.Title);
        Assert.Equal("NIP 1111111111: pozostałe nowe faktury: 15. Zobacz je w aplikacji.", summary.Body);
        Assert.Equal(new Dictionary<string, string> { ["type"] = "newInvoicesSummary", ["tenantId"] = _tenantA.ToString(), ["count"] = "15" }, summary.Data);
        Assert.Equal(11, messages.Count);
        Assert.Contains(("Więcej nowych faktur: NIP 1111111111", AddressA), messages);
        Assert.Contains("Pozostałe nowe faktury: 15. Zobacz je w aplikacji lub w portalu OpenKSeF.", await Mailpit.SnippetAsync(_mailpit, "Więcej nowych faktur: NIP 1111111111"));
    }

    [Theory]
    [InlineData(PushResult.Sent)]
    [InlineData(PushResult.InvalidToken)]
    public async Task IncrementalSync_PageTwoFailsToStore_StoredPageStillNotifies_RetryNotifiesRest(PushResult pushResult)
    {
        var page1 = Enumerable.Range(0, 8).Select(i => Bought($"A-1{i}")).ToList();
        var page2 = Enumerable.Range(0, 8).Select(i => Bought($"A-2{i}")).ToList();
        var failing = page2.Select(i => i with { VendorNip = "11111111130" }).ToList();
        var pages = new[] { page1, failing };
        _received = c => c.SubjectNip == NipA && c.PageOffset < 2 ? Page(pages[c.PageOffset]) : Page([]);
        var push = new RecordingPushProvider(pushResult);
        await using (var db = NewContext())
        {
            db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = $"user-{NipA}", Token = "fcm-token-a", Platform = Platform.Android, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.StoreFailed, (await SyncService(db, MailpitOptions(), push: push, batchSize: 8).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(8, (await Mailpit.MessagesAsync(_mailpit)).Count);
        await using (var check = NewContext())
            Assert.Equal(pushResult == PushResult.InvalidToken ? 0 : 1, await check.DeviceTokens.CountAsync());

        pages[1] = page2;
        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions(), push: push, batchSize: 8).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(16, (await Mailpit.MessagesAsync(_mailpit)).Count);
        await using var stored = NewContext();
        Assert.Equal(page1.Concat(page2).Select(i => i.KSeFNumber).Order(), (await stored.InvoiceHeaders.Where(i => i.TenantId == _tenantA).Select(i => i.KSeFInvoiceNumber).ToListAsync()).Order());
    }

    [Theory]
    [InlineData(60, 4)]
    [InlineData(5, 2)]
    public async Task IncrementalSync_CancelledDuringNotify_SendsWithinGrace_SkipsRest_NextRunSendsNothing(int graceSeconds, int expectedSent)
    {
        var invoices = Enumerable.Range(0, 4).Select(i => Bought($"A-{i}")).ToList();
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page(invoices) : Page([]);
        using var stop = new CancellationTokenSource();
        var push = new RecordingPushProvider(onSend: () =>
        {
            stop.Cancel();
            _time.Advance(TimeSpan.FromSeconds(4));
            return Task.CompletedTask;
        });
        var log = new List<(LogLevel Level, string Category, string Text)>();
        await AddDeviceAsync();

        await using (var db = NewContext())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SyncService(db, MailpitOptions(), log, push, notificationGrace: TimeSpan.FromSeconds(graceSeconds)).SyncTenantAsync(_tenantA, cancellationToken: stop.Token));

        Assert.Equal(expectedSent, push.Sent.Count);
        Assert.Equal(expectedSent, (await Mailpit.MessagesAsync(_mailpit)).Count);
        var skipped = log.Where(e => e.Category == nameof(InvoiceService) && e.Text.Contains("notifications skipped")).ToList();
        if (expectedSent == invoices.Count)
            Assert.Empty(skipped);
        else
            Assert.Contains($"skipped for {invoices.Count - expectedSent} stored invoices", Assert.Single(skipped).Text);
        await using (var check = NewContext())
        {
            var stored = await check.InvoiceHeaders.Where(i => i.TenantId == _tenantA).ToListAsync();
            Assert.Equal(invoices.Select(i => i.KSeFNumber).Order(), stored.Select(i => i.KSeFInvoiceNumber).Order());
            Assert.All(stored, i => Assert.False(i.IsPaid));
            Assert.All(stored, i => Assert.Null(i.PaidAt));
        }

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions(), log, push).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(expectedSent, push.Sent.Count);
        Assert.Equal(expectedSent, (await Mailpit.MessagesAsync(_mailpit)).Count);
    }

    [Fact]
    public async Task IncrementalSync_CancelledDuringFetchAfterOnePage_PageNotifiedWithinGrace_NextRunNotifiesOnlyNewPage()
    {
        var page1 = Enumerable.Range(0, 8).Select(i => Bought($"A-1{i}")).ToList();
        var page2 = Enumerable.Range(0, 8).Select(i => Bought($"A-2{i}")).ToList();
        using var stop = new CancellationTokenSource();
        _received = c =>
        {
            if (c.SubjectNip != NipA || c.PageOffset > 1)
                return Page([]);
            if (c.PageOffset == 0)
                return Page(page1);
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        };
        var push = new RecordingPushProvider(onSend: () =>
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        });
        var log = new List<(LogLevel Level, string Category, string Text)>();
        await AddDeviceAsync();

        await using (var db = NewContext())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SyncService(db, MailpitOptions(), log, push, batchSize: 8).SyncTenantAsync(_tenantA, cancellationToken: stop.Token));

        Assert.Equal(5, push.Sent.Count);
        Assert.Equal(5, (await Mailpit.MessagesAsync(_mailpit)).Count);
        Assert.Contains("skipped for 3 stored invoices", Assert.Single(log, e => e.Category == nameof(InvoiceService) && e.Text.Contains("notifications skipped")).Text);

        _received = c => c.SubjectNip == NipA && c.PageOffset < 2 ? Page(c.PageOffset == 0 ? page1 : page2) : Page([]);
        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions(), log, push, batchSize: 8).SyncTenantAsync(_tenantA)).Outcome);

        await using var check = NewContext();
        var numbers = await check.InvoiceHeaders.Where(i => i.TenantId == _tenantA).ToDictionaryAsync(i => i.Id.ToString(), i => i.KSeFInvoiceNumber);
        Assert.Equal(page1.Concat(page2).Select(i => i.KSeFNumber).Order(), numbers.Values.Order());
        var pushed = push.Sent.Select(n => numbers[n.Data["invoiceId"]]).ToList();
        Assert.Equal(pushed.Count, pushed.Distinct().Count());
        Assert.Equal(5, pushed.Count(n => page1.Any(i => i.KSeFNumber == n)));
        Assert.Equal(page2.Select(i => i.KSeFNumber).Order(), pushed.Where(n => page2.Any(i => i.KSeFNumber == n)).Order());
        Assert.Equal(13, (await Mailpit.MessagesAsync(_mailpit)).Count);
    }

    private async Task AddDeviceAsync()
    {
        await using var db = NewContext();
        db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = $"user-{NipA}", Token = "fcm-token-a", Platform = Platform.Android, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SameRun_RecipientRejectedBySmtpOnTenantA_StillEmailsTenantB_NoAddressInLogs()
    {
        await using var strict = Mailpit.Create(null, ("MP_SMTP_ALLOWED_RECIPIENTS", @"^b@example\.invalid$"));
        await strict.StartAsync();
        _received = c => c.PageOffset > 0 ? Page([])
            : c.SubjectNip == NipA ? Page([Bought("A-1")])
            : Page([Bought("B-1")]);
        var lastSync = DateTime.UtcNow.AddHours(-4);
        await using (var db = NewContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync")
                VALUES ({Guid.NewGuid()}, {_tenantB}, {lastSync}, {lastSync})
                """);
        var options = new EmailOptions { Enabled = true, SmtpHost = strict.Hostname, SmtpPort = strict.GetMappedPublicPort(Mailpit.SmtpPort), UseSsl = false };
        var log = new List<(LogLevel Level, string Category, string Text)>();

        await using (var db = NewContext())
        {
            var sut = SyncService(db, options, log);
            Assert.Equal(TenantSyncOutcome.Success, (await sut.SyncTenantAsync(_tenantA)).Outcome);
            Assert.Equal(TenantSyncOutcome.Success, (await sut.SyncTenantAsync(_tenantB)).Outcome);
        }

        Assert.Equal([("Nowa faktura od Dostawca", AddressB)], await Mailpit.MessagesAsync(strict));
        Assert.Single(log, e => e.Category == nameof(NotificationService) && e.Level == LogLevel.Warning && e.Text.Contains("rejected"));
        Assert.DoesNotContain(log, e => e.Text.Contains(AddressA) || e.Text.Contains(AddressB));
    }

    [Theory]
    [InlineData(false, "MP_SMTP_REQUIRE_STARTTLS")]
    [InlineData(true, "MP_SMTP_REQUIRE_TLS")]
    public async Task SendNewInvoiceEmailAsync_DeliversOverTls(bool implicitTls, string requireTls)
    {
        await using var mailpit = Mailpit.Create(
            implicitTls ? 465 : null,
            ("MP_SMTP_TLS_CERT", "sans:localhost"),
            ("MP_SMTP_TLS_KEY", "sans:localhost"),
            (requireTls, "true"));
        await mailpit.StartAsync();
        var tlsNegotiated = false;
        var sut = new EmailService(
            Options.Create(new EmailOptions
            {
                Enabled = true,
                SmtpHost = mailpit.Hostname,
                SmtpPort = implicitTls ? 465 : mailpit.GetMappedPublicPort(Mailpit.SmtpPort),
                UseSsl = true
            }),
            NullLogger<EmailService>.Instance)
        {
            ServerCertificateValidation = (_, _, _, _) => tlsNegotiated = true
        };

        await sut.SendNewInvoiceEmailAsync(AddressA, new NewInvoiceDetectedEvent(_tenantA, Guid.NewGuid(), "Dostawca", "FV/1", 123m, "PLN"));

        Assert.True(tlsNegotiated);
        Assert.Equal([("Nowa faktura od Dostawca", AddressA)], await Mailpit.MessagesAsync(mailpit));
    }

    private EmailOptions MailpitOptions() => new()
    {
        Enabled = true,
        SmtpHost = _mailpit.Hostname,
        SmtpPort = _mailpit.GetMappedPublicPort(Mailpit.SmtpPort),
        UseSsl = false
    };

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db, EmailOptions email, List<(LogLevel Level, string Category, string Text)>? log = null, IPushProvider? push = null, int batchSize = 100, TimeSpan? notificationGrace = null)
    {
        var notifications = new NotificationService(
            db,
            push is null ? [] : [push],
            new EmailService(Options.Create(email), Logger<EmailService>(log)),
            Logger<NotificationService>(log));
        return new TenantSyncService(
            db,
            _gateway,
            new InvoiceService(db, notifications, Logger<InvoiceService>(log)),
            new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
            Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero, BatchSize = batchSize, NotificationGrace = notificationGrace ?? TimeSpan.FromSeconds(5) }),
            new XmlDownloadBlocks(),
            _time,
            Logger<TenantSyncService>(log));
    }

    private static ILogger<T> Logger<T>(List<(LogLevel, string, string)>? log) =>
        log is null ? NullLogger<T>.Instance : new ListLogger<T>(log);

    private sealed class RecordingPushProvider(PushResult result = PushResult.Sent, Func<Task>? onSend = null) : IPushProvider
    {
        public List<PushNotification> Sent { get; } = [];

        public async Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
        {
            Sent.Add(notification);
            if (onSend is not null)
                await onSend();
            return result;
        }
    }

    private sealed class ListLogger<T>(List<(LogLevel, string, string)> log) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            log.Add((logLevel, typeof(T).Name, $"{formatter(state, exception)} {exception}"));
    }

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        foreach (var (id, nip, address) in new[] { (_tenantA, NipA, AddressA), (_tenantB, NipB, AddressB) })
        {
            db.Tenants.Add(new Tenant
            {
                Id = id,
                UserId = $"user-{nip}",
                Nip = nip,
                NotificationEmail = address,
                CreatedAt = now,
                UpdatedAt = now,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = id, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
            });
        }
        await db.SaveChangesAsync();
        var lastSync = now.AddHours(-4);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync")
            VALUES ({Guid.NewGuid()}, {_tenantA}, {lastSync}, {lastSync})
            """);
    }

    private static KSeFInvoiceQueryResult Page(IReadOnlyList<KSeFInvoiceHeader> invoices, DateTime? continuationAt = null) =>
        new(invoices, continuationAt);

    private static KSeFInvoiceHeader Bought(string number, DateTime? acquiredAt = null) => new(
        number, "FV 1/2026", "1111111113", "Dostawca", NipA, "Moja firma", 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), acquiredAt ?? DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");
}

[Collection(TestcontainersCollection.Name)]
public sealed class ApiEmailNotificationTests(TestcontainersFixture fixture) : IAsyncLifetime
{
    private readonly IContainer _mailpit = Mailpit.Create();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public Task DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task ApiHost_NewInvoice_EmailsNotificationAddress_WithoutDevice()
    {
        await using var factory = new OpenKSeFWebApplicationFactory(fixture).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:Enabled", "true");
            builder.UseSetting("Email:SmtpHost", _mailpit.Hostname);
            builder.UseSetting("Email:SmtpPort", _mailpit.GetMappedPublicPort(Mailpit.SmtpPort).ToString());
            builder.UseSetting("Email:UseSsl", "false");
        });
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                UserId = $"email-{tenantId}",
                Nip = "1111111111",
                NotificationEmail = "api@example.invalid",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyNewInvoiceAsync(
                new NewInvoiceDetectedEvent(tenantId, Guid.NewGuid(), "Dostawca API", "FV/2", 10m, "PLN"));

        Assert.Equal([("Nowa faktura od Dostawca API", "api@example.invalid")], await Mailpit.MessagesAsync(_mailpit));
    }
}
