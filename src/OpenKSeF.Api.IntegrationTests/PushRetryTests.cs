using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>T-088: a push owed for a new received invoice is retried by later Worker cycles until it settles, within 24 h.</summary>
public sealed class PushRetryTests : IAsyncLifetime
{
    private const string Nip = "1111111111";
    private const string UserId = "user-1";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly RecordingPushProvider _push = new();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private IReadOnlyList<KSeFInvoiceHeader> _received = [];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                return criteria.Direction == InvoiceDirection.Received
                    ? new KSeFInvoiceQueryResult(_received)
                    : new KSeFInvoiceQueryResult([], criteria.DateTo);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Xml());

        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = UserId,
            Nip = Nip,
            NotificationEmail = "owner@example.invalid",
            CreatedAt = now,
            UpdatedAt = now,
            KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
        });
        db.TenantMembers.Add(new TenantMember { TenantId = _tenantId, UserId = UserId, Role = TenantRole.Owner, CreatedAt = now });
        db.SyncStates.Add(new SyncState { Id = Guid.NewGuid(), TenantId = _tenantId, LastSyncedAt = now.AddHours(-4), LastSuccessfulSync = now.AddHours(-4) });
        db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "fcm-token-1", Platform = Platform.Android, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db) => new(
        db,
        _gateway,
        new InvoiceService(db, new NotificationService(db, [_push], _email, NullLogger<NotificationService>.Instance), NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero }),
        new KSeFRequestLimits(),
        TimeProvider.System,
        NullLogger<TenantSyncService>.Instance);

    private async Task WorkerCycleAsync(IReadOnlyList<KSeFInvoiceHeader>? received = null)
    {
        _received = received ?? [];
        await using var db = NewContext();
        var results = await SyncService(db).SyncAllTenantsAsync();
        Assert.Equal(TenantSyncOutcome.Success, Assert.Single(results).Outcome);
    }

    private async Task<List<InvoiceHeader>> RowsAsync()
    {
        await using var db = NewContext();
        return await db.InvoiceHeaders.AsNoTracking().ToListAsync();
    }

    private static KSeFInvoiceHeader Bought(string number) => new(
        number, "FV 1/2026", "7740001454", "Dostawca", Nip, "Moja firma", 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");

    private static byte[] Xml() => Encoding.UTF8.GetBytes(
        $"""<Faktura xmlns="{Fa3}"><Fa><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

    private int NewInvoiceEmails() => _email.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IEmailService.SendNewInvoiceEmailAsync));

    [Fact]
    public async Task RelayOutage_ThenRecovery_DeliversThePushOnce_EmailOnce()
    {
        _push.Result = PushResult.Failed;
        await WorkerCycleAsync([Bought("NEW-1")]);

        Assert.Null(Assert.Single(await RowsAsync()).NotifiedAt);
        Assert.Equal(0, _push.Sent.Count);

        _push.Result = PushResult.Sent;
        await WorkerCycleAsync();
        await WorkerCycleAsync();

        Assert.Single(_push.Sent);
        Assert.Equal("Nowa faktura", _push.Sent[0].Title);
        Assert.NotNull(Assert.Single(await RowsAsync()).NotifiedAt);
        Assert.Equal(1, NewInvoiceEmails());
    }

    [Fact]
    public async Task NormalPath_PushesOnce_LaterCyclesSendNothing()
    {
        await WorkerCycleAsync([Bought("NEW-1")]);
        await WorkerCycleAsync();
        await WorkerCycleAsync();

        Assert.Equal(1, _push.Attempts);
        Assert.Equal(1, NewInvoiceEmails());
        Assert.NotNull(Assert.Single(await RowsAsync()).NotifiedAt);
    }

    [Fact]
    public async Task OverCap_Outage_ThenRecovery_TenSinglesAndOneSummary_AllStamped_NoThirdSend()
    {
        var page = Enumerable.Range(0, 12).Select(i => Bought($"NEW-{i:D2}")).ToList();
        _push.Result = PushResult.Failed;
        await WorkerCycleAsync(page);
        Assert.Equal(11, _push.Attempts);

        _push.Result = PushResult.Sent;
        await WorkerCycleAsync();
        await WorkerCycleAsync();

        Assert.Equal(10, _push.Sent.Count(n => n.Title == "Nowa faktura"));
        var summary = Assert.Single(_push.Sent, n => n.Title == "Więcej nowych faktur");
        Assert.Equal("2", summary.Data["count"]);
        Assert.Equal(11, _push.Sent.Count);
        Assert.All(await RowsAsync(), r => Assert.NotNull(r.NotifiedAt));
    }

    [Fact]
    public async Task InitialSync_AndForcedResync_StoreRowsAsNotified_NeverPush()
    {
        await using (var db = NewContext())
            await db.SyncStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSuccessfulSync, (DateTime?)null));

        await WorkerCycleAsync([Bought("OLD-1")]);

        _received = [Bought("OLD-1"), Bought("OLD-2")];
        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, forceFullResync: true)).Outcome);

        await WorkerCycleAsync();

        Assert.Equal(0, _push.Attempts);
        var rows = await RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.FirstSeenAt, r.NotifiedAt));
    }

    [Fact]
    public async Task OwedRowOlderThan24h_IsStampedNotPushed_YoungerOneIs()
    {
        await WorkerCycleAsync([Bought("STALE"), Bought("FRESH")]);
        await using (var db = NewContext())
        {
            await db.InvoiceHeaders.ExecuteUpdateAsync(s => s.SetProperty(i => i.NotifiedAt, (DateTime?)null));
            await db.InvoiceHeaders.Where(i => i.KSeFInvoiceNumber == "STALE")
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.FirstSeenAt, DateTime.UtcNow.AddHours(-25)));
        }
        _push.Sent.Clear();

        await WorkerCycleAsync();

        var sent = Assert.Single(_push.Sent);
        var rows = (await RowsAsync()).ToDictionary(r => r.KSeFInvoiceNumber);
        Assert.Equal(rows["FRESH"].Id.ToString(), sent.Data["invoiceId"]);
        Assert.NotNull(rows["STALE"].NotifiedAt);
        Assert.NotNull(rows["FRESH"].NotifiedAt);

        await WorkerCycleAsync();
        Assert.Single(_push.Sent);
    }

    [Fact]
    public async Task OwedIssuedRow_IsNeverPushed()
    {
        await WorkerCycleAsync([Bought("SOLD")]);
        await using (var db = NewContext())
            await db.InvoiceHeaders.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Direction, InvoiceDirection.Issued)
                .SetProperty(i => i.NotifiedAt, (DateTime?)null));
        _push.Sent.Clear();

        await WorkerCycleAsync();
        await WorkerCycleAsync();

        Assert.Empty(_push.Sent);
        Assert.Null(Assert.Single(await RowsAsync()).NotifiedAt);
    }

    private sealed class RecordingPushProvider : IPushProvider
    {
        public PushResult Result { get; set; } = PushResult.Sent;

        public int Attempts { get; private set; }

        public List<PushNotification> Sent { get; } = [];

        public Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
        {
            Attempts++;
            if (Result == PushResult.Sent)
                Sent.Add(notification);
            return Task.FromResult(Result);
        }
    }
}
