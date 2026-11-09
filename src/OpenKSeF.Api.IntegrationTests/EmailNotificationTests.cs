using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

internal static class Mailpit
{
    public const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    public static IContainer Create(int? hostSmtpPort = null, params (string Name, string Value)[] environment)
    {
        var builder = new ContainerBuilder()
            .WithImage("axllent/mailpit:v1.30.0")
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

    [Theory]
    [InlineData(6, 1)]
    [InlineData(8, 0)]
    public async Task IncrementalSync_AfterGapOverSevenDays_StoresInvoiceWithoutEmailOrPush(int gapDays, int expectedNotifications)
    {
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1")]) : Page([]);
        var push = Substitute.For<IPushProvider>();
        push.SendAsync(Arg.Any<string>(), Arg.Any<Platform>(), Arg.Any<PushNotification>()).Returns(PushResult.Sent);
        var lastSync = DateTime.UtcNow.AddDays(-gapDays);
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "SyncStates" SET "LastSyncedAt" = {lastSync}, "LastSuccessfulSync" = {lastSync} WHERE "TenantId" = {_tenantA}
                """);
            var now = DateTime.UtcNow;
            db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = $"user-{NipA}", Token = "fcm-token", Platform = Platform.Android, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db, MailpitOptions(), push).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(expectedNotifications, (await Mailpit.MessagesAsync(_mailpit)).Count);
        Assert.Equal(expectedNotifications, push.ReceivedCalls().Count());
        await using var check = NewContext();
        Assert.Equal(1, await check.InvoiceHeaders.CountAsync(i => i.KSeFInvoiceNumber == "A-1"));
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

    private TenantSyncService SyncService(ApplicationDbContext db, EmailOptions email, params IPushProvider[] push)
    {
        var notifications = new NotificationService(
            db,
            push,
            new EmailService(Options.Create(email), NullLogger<EmailService>.Instance),
            NullLogger<NotificationService>.Instance);
        return new TenantSyncService(
            db,
            _gateway,
            Substitute.For<IEncryptionService>(),
            new InvoiceService(db, notifications, NullLogger<InvoiceService>.Instance),
            new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
            Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero }),
            new XmlDownloadBlocks(),
            NullLogger<TenantSyncService>.Instance);
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
        new(invoices, invoices.Count, 0, 100, continuationAt);

    private static KSeFInvoiceHeader Bought(string number) => new(
        number, number, "FV 1/2026", "1111111113", "Dostawca", NipA, "Moja firma", 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, "Vat",
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
