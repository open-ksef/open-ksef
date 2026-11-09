using System.Text;
using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class InvoiceXmlQueueTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261104125258_AddForeignServiceInvoice";
    private const string UserId = "user-1";
    private const string Nip = "1234563218";
    private const string Account = "PL61109010140000071219812874";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly DateTime PaidAt = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastSync = DateTime.UtcNow.AddHours(-4);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly XmlDownloadBlocks _blocks = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<string> _calls = [];
    private readonly List<string> _downloads = [];

    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _received = _ => Page([]);
    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _issued = c => Page([], c.DateTo);
    private Func<string, byte[]> _xml = _ => Xml("2026-11-20");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                _calls.Add($"query:{criteria.Direction}");
                return criteria.Direction == InvoiceDirection.Received ? _received(criteria) : _issued(criteria);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var number = call.Arg<string>();
                _calls.Add("download");
                _downloads.Add(number);
                return _xml(number);
            });

        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await AddTenantAsync(db, _tenantId, UserId, Nip);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync")
            VALUES ({Guid.NewGuid()}, {_tenantId}, {LastSync}, {LastSync})
            """);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db, TimeSpan? spacing = null, int budget = 60) => new(
        db,
        _gateway,
        Substitute.For<IEncryptionService>(),
        new InvoiceService(db, _notifications, NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { XmlDownloadSpacing = spacing ?? TimeSpan.Zero, XmlDownloadsPerCycle = budget }),
        _blocks,
        NullLogger<TenantSyncService>.Instance);

    private async Task MigrateAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private async Task<IReadOnlyList<TenantSyncResult>> WorkerCycleAsync(TimeSpan? spacing = null, int budget = 60)
    {
        await using var db = NewContext();
        return await SyncService(db, spacing, budget).SyncAllTenantsAsync();
    }

    private async Task<TenantSyncResult> ManualSyncAsync(bool fullResync = false)
    {
        await using var db = NewContext();
        return await SyncService(db).SyncTenantAsync(_tenantId, UserId, fullResync);
    }

    private static async Task AddTenantAsync(ApplicationDbContext db, Guid id, string userId, string nip)
    {
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = id,
            UserId = userId,
            Nip = nip,
            CreatedAt = now,
            UpdatedAt = now,
            KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = id, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
        });
        await db.SaveChangesAsync();
    }

    private async Task InsertAsync(
        string number,
        int daysAgo = 10,
        bool isPaid = false,
        DateOnly? dueDate = null,
        string? account = null,
        bool hasLine = true,
        InvoiceDirection direction = InvoiceDirection.Received,
        Guid? tenantId = null)
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        var issued = DateTime.UtcNow.AddDays(-daysAgo);
        var paidAt = isPaid ? (DateTime?)PaidAt : null;
        var directionText = direction.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt", "DueDate", "VendorBankAccount", "Direction")
            VALUES ({id}, {tenantId ?? _tenantId}, {number}, {number}, 'Dostawca', '5261040828', 100, 23, 123, {issued}, {issued}, {issued}, {isPaid}, {paidAt}, {dueDate}, {account}, {directionText})
            """);
        if (hasLine)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InvoiceLines" ("Id", "InvoiceHeaderId", "LineNumber", "Name", "Quantity", "UnitPriceNet", "UnitPriceGross", "AmountNet", "AmountGross", "AmountVat")
                VALUES ({Guid.NewGuid()}, {id}, 1, 'Towar stary', 1, 100, 123, 100, 123, 23)
                """);
        }
    }

    private static KSeFInvoiceQueryResult Page(IReadOnlyList<KSeFInvoiceHeader> invoices, DateTime? continuationAt = null, bool truncated = false) =>
        new(invoices, invoices.Count, 0, 100, continuationAt, truncated);

    private static KSeFInvoiceHeader Bought(string number) => new(
        number, number, "FV 1/2026", "5261040828", "Dostawca", Nip, "Moja firma", 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");

    private static KSeFInvoiceHeader Sale(string number) => new(
        number, number, "FV 7/2026", Nip, "Moja firma", "5261040828", "Nabywca", 200m, 46m, 246m, "PLN", DateTime.UtcNow.AddDays(-5), DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");

    private static byte[] Xml(string? dueDate)
    {
        var term = dueDate is null ? "" : $"<TerminPlatnosci><Termin>{dueDate}</Termin></TerminPlatnosci>";
        return Encoding.UTF8.GetBytes(
            $"""<Faktura xmlns="{Fa3}"><Fa><Platnosc>{term}<RachunekBankowy><NrRB>{Account}</NrRB></RachunekBankowy></Platnosc><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar nowy</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");
    }

    private static KsefRateLimitException RateLimited(int retryAfterSeconds) => new("429", retryAfterSeconds);

    private sealed record RowSnapshot(Guid Id, string Number, bool IsPaid, DateTime? PaidAt, DateOnly? DueDate, string? VendorBankAccount, string Lines, InvoiceDirection Direction, DateTime? XmlFetchedAt);

    private async Task<List<RowSnapshot>> SnapshotAsync()
    {
        await using var db = NewContext();
        var rows = await db.InvoiceHeaders.AsNoTracking().Include(i => i.Lines).OrderBy(i => i.KSeFInvoiceNumber).ToListAsync();
        return rows.Select(i => new RowSnapshot(
            i.Id, i.KSeFInvoiceNumber, i.IsPaid, i.PaidAt, i.DueDate, i.VendorBankAccount,
            string.Join('|', i.Lines.OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Name}:{l.AmountGross}")),
            i.Direction, i.XmlFetchedAt)).ToList();
    }

    private async Task<InvoiceHeader> RowAsync(string number)
    {
        await using var db = NewContext();
        return await db.InvoiceHeaders.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.KSeFInvoiceNumber == number);
    }

    [Fact]
    public void Defaults_StayInsideKSeFLimits()
    {
        var options = new TenantSyncOptions();

        Assert.True(options.XmlDownloadsPerCycle <= 64);
        Assert.True(TimeSpan.FromMinutes(1) / options.XmlDownloadSpacing <= 16);
        Assert.True(options.XmlDownloadsPerManualRun < 16);
    }

    [Fact]
    public async Task Migration_IsAdditive_AndExistingRowsKeepEveryValue()
    {
        await InsertAsync("KSEF-PAID", isPaid: true, dueDate: new DateOnly(2026, 10, 15), account: Account);
        await InsertAsync("KSEF-UNPAID");
        await using (var db = NewContext())
        {
            var before = await db.Database.SqlQueryRaw<string>("""SELECT "KSeFInvoiceNumber" || "IsPaid" || coalesce("PaidAt"::text, '') || coalesce("DueDate"::text, '') || coalesce("VendorBankAccount", '') AS "Value" FROM "InvoiceHeaders" ORDER BY 1""").ToListAsync();

            await db.Database.MigrateAsync();

            var after = await db.Database.SqlQueryRaw<string>("""SELECT "KSeFInvoiceNumber" || "IsPaid" || coalesce("PaidAt"::text, '') || coalesce("DueDate"::text, '') || coalesce("VendorBankAccount", '') AS "Value" FROM "InvoiceHeaders" ORDER BY 1""").ToListAsync();
            Assert.Equal(before, after);
            Assert.All(await db.InvoiceHeaders.ToListAsync(), r =>
            {
                Assert.Null(r.XmlFetchedAt);
                Assert.Null(r.Settlement);
                Assert.Null(r.SettlementAmount);
                Assert.Null(r.SplitPayment);
                Assert.Null(r.SellerPaidOn);
                Assert.Null(r.PartialPayment);
                Assert.Null(r.FactorBankAccount);
                Assert.Null(r.FactorNip);
                Assert.Null(r.FactorName);
            });
            Assert.Equal(2, await db.InvoiceLines.CountAsync());
        }
    }

    [Fact]
    public async Task WorkerCycle_FollowsPriority_NewestFirst_AndSkipsRowsWithNothingToGain()
    {
        await InsertAsync("ISSUED-NO-LINES", daysAgo: 1, hasLine: false, direction: InvoiceDirection.Issued);
        await InsertAsync("ISSUED-WITH-LINES", daysAgo: 1, direction: InvoiceDirection.Issued);
        await InsertAsync("UNDATED-OLD", daysAgo: 40);
        await InsertAsync("UNDATED-RECENT", daysAgo: 20);
        await InsertAsync("PAID-UNDATED-WITH-LINES", daysAgo: 1, isPaid: true);
        await InsertAsync("PAID-NO-LINES", daysAgo: 1, isPaid: true, hasLine: false);
        await InsertAsync("DATED-WITH-LINES-NO-ACCOUNT", daysAgo: 1, dueDate: new DateOnly(2026, 12, 1));
        await InsertAsync("UNDATED-ALREADY-READ", daysAgo: 2);
        await MigrateAsync();
        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "XmlFetchedAt" = now() - interval '30 days' WHERE "KSeFInvoiceNumber" = 'UNDATED-ALREADY-READ'""");
        _received = _ => Page([Bought("NEW-RECEIVED")]);

        await WorkerCycleAsync();

        Assert.Equal(["NEW-RECEIVED", "UNDATED-RECENT", "UNDATED-OLD", "ISSUED-NO-LINES", "DATED-WITH-LINES-NO-ACCOUNT", "UNDATED-ALREADY-READ", "PAID-NO-LINES"], _downloads);
    }

    [Fact]
    public async Task WorkerCycle_MetadataOfAllTenantsRunsBeforeAnyDownload_NewInvoiceIsPushedFirst()
    {
        var secondTenant = Guid.NewGuid();
        await using (var db = NewContext())
            await AddTenantAsync(db, secondTenant, "user-2", "5261040828");
        await InsertAsync("UNDATED");
        await InsertAsync("UNDATED-2", tenantId: secondTenant);
        await MigrateAsync();
        _received = c => Page(c.SubjectNip == Nip ? [Bought("NEW")] : []);
        _xml = _ => throw RateLimited(120);
        var issuedSubjects = new HashSet<string>();
        _issued = c =>
        {
            issuedSubjects.Add(c.SubjectNip);
            return Page([], c.DateTo);
        };

        var results = await WorkerCycleAsync();

        Assert.All(results, r => Assert.Equal(TenantSyncOutcome.Success, r.Outcome));
        var firstDownload = _calls.IndexOf("download");
        Assert.True(firstDownload >= 0);
        Assert.DoesNotContain(_calls.Skip(firstDownload), c => c.StartsWith("query", StringComparison.Ordinal));
        Assert.Equal(new HashSet<string> { Nip, "5261040828" }, issuedSubjects);
        await _notifications.Received(1).NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task WorkerCycle_BudgetOf60_IsSharedByTwoTenantsOfOneNip_NewInvoicesFirstAcrossThem()
    {
        var secondTenant = Guid.NewGuid();
        await using (var db = NewContext())
            await AddTenantAsync(db, secondTenant, "user-2", Nip);
        for (var n = 0; n < 40; n++)
        {
            await InsertAsync($"A-{n:00}", daysAgo: 10 + n);
            await InsertAsync($"B-{n:00}", daysAgo: 10 + n, tenantId: secondTenant);
        }

        await MigrateAsync();
        _received = _ => Page(_calls.Count(c => c == "query:Received") == 2 ? [Bought("NEW-IN-SECOND")] : []);

        await WorkerCycleAsync();

        Assert.Equal(60, _downloads.Count);
        Assert.Equal(60, _downloads.Distinct().Count());
        Assert.Equal("NEW-IN-SECOND", _downloads[0]);
        Assert.Contains(_downloads, d => d.StartsWith("A-", StringComparison.Ordinal));
        Assert.Contains(_downloads, d => d.StartsWith("B-", StringComparison.Ordinal));

        _downloads.Clear();
        await WorkerCycleAsync();

        Assert.Equal(21, _downloads.Count);
    }

    [Fact]
    public async Task WorkerCycle_BudgetsOfDifferentNips_AreSeparate()
    {
        var secondTenant = Guid.NewGuid();
        await using (var db = NewContext())
            await AddTenantAsync(db, secondTenant, "user-2", "5261040828");
        for (var n = 0; n < 61; n++)
        {
            await InsertAsync($"A-{n:00}");
            await InsertAsync($"B-{n:00}", tenantId: secondTenant);
        }

        await MigrateAsync();

        await WorkerCycleAsync();

        Assert.Equal(60, _downloads.Count(d => d.StartsWith("A-", StringComparison.Ordinal)));
        Assert.Equal(60, _downloads.Count(d => d.StartsWith("B-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WorkerCycle_PacesDownloads()
    {
        await InsertAsync("A");
        await InsertAsync("B");
        await InsertAsync("C");
        await MigrateAsync();

        var started = System.Diagnostics.Stopwatch.StartNew();
        await WorkerCycleAsync(TimeSpan.FromMilliseconds(300));

        Assert.Equal(3, _downloads.Count);
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(590), started.Elapsed.ToString());
    }

    [Fact]
    public async Task ManualSync_Spaces_Downloads_By150Ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(150), new TenantSyncOptions().XmlManualDownloadSpacing);
        await InsertAsync("A");
        await InsertAsync("B");
        await InsertAsync("C");
        await MigrateAsync();

        var started = System.Diagnostics.Stopwatch.StartNew();
        await ManualSyncAsync();

        Assert.Equal(3, _downloads.Count);
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(290), started.Elapsed.ToString());
    }

    [Fact]
    public async Task NotReadRow_WithLinesAccountAndMarker_IncrementalRereadsIt_LinesNotDuplicated_PaidUntouched_NoNotification()
    {
        await InsertAsync("READ-BEFORE", account: "DE12000000000000000001", dueDate: new DateOnly(2026, 11, 1));
        await InsertAsync("PAID-READ-BEFORE", isPaid: true, account: Account, dueDate: new DateOnly(2026, 10, 15));
        await MigrateAsync();
        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "XmlFetchedAt" = now() - interval '30 days' """);
        var before = await RowAsync("READ-BEFORE");
        var paidBefore = await RowAsync("PAID-READ-BEFORE");
        _xml = _ => Encoding.UTF8.GetBytes(
            $"""<Faktura xmlns="{Fa3}"><Fa><P_15>123.00</P_15><Adnotacje><P_18A>1</P_18A></Adnotacje><Rozliczenie><SumaOdliczen>23.00</SumaOdliczen><DoZaplaty>100.00</DoZaplaty></Rozliczenie><Platnosc><TerminPlatnosci><Termin>2026-12-24</Termin></TerminPlatnosci><RachunekBankowy><NrRB>DE12000000000000000001</NrRB></RachunekBankowy><RachunekBankowy><NrRB>{Account}</NrRB></RachunekBankowy></Platnosc><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar nowy</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz><FaWiersz><NrWierszaFa>2</NrWierszaFa><P_7>Towar drugi</P_7><P_11>1</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

        await WorkerCycleAsync();

        Assert.Equal(["READ-BEFORE"], _downloads);
        var row = await RowAsync("READ-BEFORE");
        Assert.Equal(SettlementState.Confirmed, row.Settlement);
        Assert.Equal(100m, row.SettlementAmount);
        Assert.True(row.SplitPayment);
        Assert.Equal(Account, row.VendorBankAccount);
        Assert.Equal(new DateOnly(2026, 11, 1), row.DueDate);
        Assert.Equal(before.Lines.Single().Id, row.Lines.Single().Id);
        Assert.Equal("Towar stary", row.Lines.Single().Name);
        Assert.False(row.IsPaid);
        Assert.Null(row.PaidAt);
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());

        _downloads.Clear();
        await WorkerCycleAsync();
        Assert.Empty(_downloads);

        await ManualSyncAsync(fullResync: true);

        Assert.Equal(["PAID-READ-BEFORE"], _downloads);
        var paid = await RowAsync("PAID-READ-BEFORE");
        Assert.Equal(SettlementState.Confirmed, paid.Settlement);
        Assert.True(paid.IsPaid);
        Assert.Equal(paidBefore.PaidAt, paid.PaidAt);
        Assert.Equal(paidBefore.Lines.Single().Id, paid.Lines.Single().Id);
        await using (var db = NewContext())
            Assert.Equal(2, await db.InvoiceLines.CountAsync());
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task NotReadRow_ParseOrDownloadFails_StaysNotRead_NeverNone()
    {
        await InsertAsync("PARSE-FAILS", account: Account, dueDate: new DateOnly(2026, 11, 1));
        await InsertAsync("DOWNLOAD-FAILS", account: Account, dueDate: new DateOnly(2026, 11, 1));
        await MigrateAsync();
        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "XmlFetchedAt" = now() - interval '30 days' """);
        var before = await SnapshotAsync();
        _xml = number => number == "PARSE-FAILS" ? Encoding.UTF8.GetBytes("<not-xml") : throw new HttpRequestException("500");

        await ManualSyncAsync();

        Assert.Equal(2, _downloads.Count);
        Assert.Null((await RowAsync("PARSE-FAILS")).Settlement);
        Assert.Null((await RowAsync("DOWNLOAD-FAILS")).Settlement);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task NoTerminInXml_IsMarked_AndNotDownloadedAgainByWorkerManualOrFullResync()
    {
        await InsertAsync("NO-TERM", account: Account);
        await MigrateAsync();
        _xml = _ => Xml(null);
        var linesBefore = (await RowAsync("NO-TERM")).Lines.Single().Id;

        await WorkerCycleAsync();

        var row = await RowAsync("NO-TERM");
        Assert.Null(row.DueDate);
        Assert.NotNull(row.XmlFetchedAt);
        Assert.Equal(linesBefore, row.Lines.Single().Id);
        Assert.Single(_downloads);

        await WorkerCycleAsync();
        await ManualSyncAsync();
        await ManualSyncAsync(fullResync: true);

        Assert.Single(_downloads);
    }

    [Fact]
    public async Task Fake429_StopsAtOnce_ChangesNothing_RunSucceeds_IssuedMetadataStored_HonoursRetryAfter()
    {
        await InsertAsync("PAID-NO-LINES", daysAgo: 1, isPaid: true, account: Account, hasLine: false);
        await InsertAsync("UNDATED-1", daysAgo: 2, account: Account);
        await InsertAsync("UNDATED-2", daysAgo: 3);
        await InsertAsync("UNDATED-3", daysAgo: 4);
        await MigrateAsync();
        var before = await SnapshotAsync();
        _issued = c => Page(c.DateFrom < DateTime.UtcNow.AddMonths(-11) ? [Sale("SALE-1")] : [], c.DateTo);
        _xml = _ => throw RateLimited(3600);

        var results = await WorkerCycleAsync();

        Assert.Equal(TenantSyncOutcome.Success, Assert.Single(results).Outcome);
        Assert.Single(_downloads);
        Assert.Equal(before, (await SnapshotAsync()).Where(r => r.Number != "SALE-1").ToList());
        Assert.Equal(InvoiceDirection.Issued, (await RowAsync("SALE-1")).Direction);

        _downloads.Clear();
        await WorkerCycleAsync();
        var manual = await ManualSyncAsync();

        Assert.Empty(_downloads);
        Assert.Equal(5, manual.FailedDownloads);
        Assert.Equal(5, manual.QueuedDownloads);
        Assert.Equal(0, manual.ErroredDownloads);
    }

    [Fact]
    public async Task After429ExpiresNextCycle_ContinuesWithRemainingRows_NeverRestarts()
    {
        await InsertAsync("U-1", daysAgo: 1);
        await InsertAsync("U-2", daysAgo: 2);
        await InsertAsync("U-3", daysAgo: 3);
        await MigrateAsync();
        var served = 0;
        _xml = _ => ++served == 2 ? throw RateLimited(0) : Xml("2026-11-20");

        await WorkerCycleAsync();

        Assert.Equal(["U-1", "U-2"], _downloads);
        Assert.NotNull((await RowAsync("U-1")).XmlFetchedAt);
        Assert.Null((await RowAsync("U-2")).DueDate);

        _downloads.Clear();
        await WorkerCycleAsync();

        Assert.Equal(["U-2", "U-3"], _downloads);
        Assert.Equal(new DateOnly(2026, 11, 20), (await RowAsync("U-3")).DueDate);
    }

    [Fact]
    public async Task ThreeFailedDownloadsInARow_StopTheNip_BeforeTheClientBreakerWouldOpen()
    {
        for (var n = 0; n < 6; n++)
            await InsertAsync($"U-{n}");
        await MigrateAsync();
        var before = await SnapshotAsync();
        _xml = _ => throw new HttpRequestException("503");

        var results = await WorkerCycleAsync();

        Assert.Equal(TenantSyncOutcome.Success, Assert.Single(results).Outcome);
        Assert.Equal(3, _downloads.Count);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task ManualSync_SeparatesRowsThatFailedFromRowsStillQueued()
    {
        for (var n = 0; n < 6; n++)
            await InsertAsync($"U-{n}");
        await MigrateAsync();
        _xml = _ => throw new HttpRequestException("503");

        var manual = await ManualSyncAsync();

        Assert.Equal(3, manual.ErroredDownloads);
        Assert.Equal(3, manual.QueuedDownloads);
        Assert.Equal(6, manual.FailedDownloads);
    }

    [Fact]
    public async Task ParseFailureOrFailedDownload_KeepsLinesDueDateAccountAndPaidFlags_AndRetriesLater()
    {
        await InsertAsync("PARSE-FAILS", daysAgo: 1, isPaid: false, account: Account);
        await InsertAsync("DOWNLOAD-FAILS", daysAgo: 2, account: Account);
        await InsertAsync("PAID-NO-LINES", daysAgo: 3, isPaid: true, dueDate: new DateOnly(2026, 10, 15), account: Account, hasLine: false);
        await MigrateAsync();
        var before = await SnapshotAsync();
        _xml = number => number switch
        {
            "PARSE-FAILS" => Encoding.UTF8.GetBytes("<not-xml"),
            "DOWNLOAD-FAILS" => throw new HttpRequestException("500"),
            _ => Encoding.UTF8.GetBytes($"<Faktura xmlns=\"{Fa3}\"/>")
        };

        var manual = await ManualSyncAsync();

        Assert.Equal(3, manual.FailedDownloads);
        Assert.Equal(3, manual.ErroredDownloads);
        Assert.Equal(0, manual.QueuedDownloads);
        Assert.Equal(before, await SnapshotAsync());

        _downloads.Clear();
        _xml = _ => Xml("2026-11-20");
        await ManualSyncAsync();

        Assert.Equal(3, _downloads.Count);
        var fixedRow = await RowAsync("PARSE-FAILS");
        Assert.Equal(new DateOnly(2026, 11, 20), fixedRow.DueDate);
        Assert.Equal("Towar stary", fixedRow.Lines.Single().Name);
    }

    [Fact]
    public async Task Backfill_ChangesOnlyDueDateAndMarker_OnPaidAndUnpaidRows()
    {
        await InsertAsync("UNPAID", account: Account);
        await InsertAsync("PAID", isPaid: true, account: Account);
        await MigrateAsync();
        var before = await SnapshotAsync();

        var result = await ManualSyncAsync(fullResync: true);

        Assert.Equal(0, result.FailedDownloads);
        var after = await SnapshotAsync();
        Assert.All(after, r => Assert.Equal(new DateOnly(2026, 11, 20), r.DueDate));
        Assert.All(after, r => Assert.NotNull(r.XmlFetchedAt));
        Assert.Equal(before, after.Select((r, i) => r with { DueDate = before[i].DueDate, XmlFetchedAt = null }).ToList());
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task IssuedRow_GetsLinesOnly_NoNotification_ReceivedQueriesSeeNothing()
    {
        await MigrateAsync();
        _issued = c => Page(c.DateFrom < DateTime.UtcNow.AddMonths(-11) ? [Sale("SALE-1")] : [], c.DateTo);

        await WorkerCycleAsync();

        var sale = await RowAsync("SALE-1");
        Assert.Equal("Towar nowy", sale.Lines.Single().Name);
        Assert.Null(sale.DueDate);
        Assert.Null(sale.VendorBankAccount);
        Assert.False(sale.IsPaid);
        Assert.NotNull(sale.XmlFetchedAt);
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());

        await using var db = NewContext();
        Assert.False(await db.InvoiceHeaders.AnyAsync(i => i.Direction == InvoiceDirection.Received));

        _downloads.Clear();
        await WorkerCycleAsync();
        Assert.Empty(_downloads);
    }

    [Fact]
    public async Task FullResync_RereadsDatedRowKeepingItsDueDate_FillsUndatedAndReportsFailures()
    {
        await InsertAsync("DATED", dueDate: new DateOnly(2026, 11, 1), account: Account);
        await InsertAsync("UNDATED-OK", daysAgo: 1);
        await InsertAsync("UNDATED-BAD", daysAgo: 2);
        await MigrateAsync();
        _received = _ => Page([Bought("DATED"), Bought("UNDATED-OK"), Bought("UNDATED-BAD")]);
        _xml = number => number == "UNDATED-BAD" ? throw new HttpRequestException("500") : Xml("2026-11-20");

        var result = await ManualSyncAsync(fullResync: true);

        Assert.Contains("DATED", _downloads);
        Assert.Equal(TenantSyncOutcome.Success, result.Outcome);
        Assert.Equal(1, result.FailedDownloads);
        var dated = await RowAsync("DATED");
        Assert.Equal(new DateOnly(2026, 11, 1), dated.DueDate);
        Assert.Equal(SettlementState.None, dated.Settlement);
        Assert.Null((await RowAsync("UNDATED-BAD")).Settlement);

        _downloads.Clear();
        await ManualSyncAsync(fullResync: true);
        Assert.Equal(["UNDATED-BAD"], _downloads);
    }

    [Fact]
    public async Task ManualSync_Downloads15_LeavesTheRestToTheWorker_ReportsThem_AndStopsOn429()
    {
        for (var n = 0; n < 20; n++)
            await InsertAsync($"U-{n:00}");
        await MigrateAsync();

        var manual = await ManualSyncAsync();

        Assert.Equal(15, _downloads.Count);
        Assert.Equal(5, manual.FailedDownloads);
        Assert.Equal(5, manual.QueuedDownloads);
        Assert.Equal(0, manual.ErroredDownloads);
        Assert.Equal(TenantSyncOutcome.Success, manual.Outcome);

        _downloads.Clear();
        await WorkerCycleAsync();

        Assert.Equal(5, _downloads.Count);

        for (var n = 0; n < 5; n++)
            await InsertAsync($"V-{n}");
        _downloads.Clear();
        _xml = _ => throw RateLimited(3600);

        var stopped = await ManualSyncAsync();

        Assert.Single(_downloads);
        Assert.Equal(5, stopped.FailedDownloads);
        Assert.Equal(5, stopped.QueuedDownloads);
        Assert.Equal(0, stopped.ErroredDownloads);
        Assert.Equal(TenantSyncOutcome.Success, stopped.Outcome);
    }

    [Fact]
    public async Task RowsThatAlwaysFail_MoveBehindOthers_AndCannotEatTheBudget()
    {
        await InsertAsync("BAD-PARSE-1", daysAgo: 1);
        await InsertAsync("BAD-PARSE-2", daysAgo: 2);
        await InsertAsync("GOOD", daysAgo: 3);
        await MigrateAsync();
        var before = await SnapshotAsync();
        _xml = number => number.StartsWith("BAD", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("<not-xml") : Xml("2026-11-20");

        await WorkerCycleAsync(budget: 2);

        Assert.Equal(["BAD-PARSE-1", "BAD-PARSE-2"], _downloads);
        Assert.Equal(before, await SnapshotAsync());
        Assert.NotNull((await RowAsync("BAD-PARSE-1")).XmlAttemptedAt);
        Assert.Null((await RowAsync("BAD-PARSE-1")).XmlFetchedAt);

        _downloads.Clear();
        await WorkerCycleAsync(budget: 2);

        Assert.Equal(["GOOD", "BAD-PARSE-1"], _downloads);
        Assert.Equal(new DateOnly(2026, 11, 20), (await RowAsync("GOOD")).DueDate);
    }

    [Fact]
    public async Task RowThatAlwaysFailsToRead_SkippedByIncrementalAfterCap_RetriedOnFullResync()
    {
        await InsertAsync("BAD-PARSE", daysAgo: 1);
        await InsertAsync("NET-DOWN", daysAgo: 2);
        await MigrateAsync();
        var before = await SnapshotAsync();
        _xml = number => number == "BAD-PARSE" ? Encoding.UTF8.GetBytes("<not-xml") : throw new HttpRequestException("500");

        for (var cycle = 0; cycle < 7; cycle++)
            await WorkerCycleAsync();

        Assert.Equal(5, _downloads.Count(n => n == "BAD-PARSE"));
        Assert.Equal(7, _downloads.Count(n => n == "NET-DOWN"));
        Assert.Equal((short)5, (await RowAsync("BAD-PARSE")).XmlFailedReads);
        Assert.Null((await RowAsync("NET-DOWN")).XmlFailedReads);
        Assert.Equal(before, await SnapshotAsync());

        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "XmlAttemptedAt" = now() - interval '6 days' WHERE "KSeFInvoiceNumber" = 'BAD-PARSE'""");
        _downloads.Clear();
        await WorkerCycleAsync();
        Assert.DoesNotContain("BAD-PARSE", _downloads);

        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "XmlAttemptedAt" = now() - interval '8 days' WHERE "KSeFInvoiceNumber" = 'BAD-PARSE'""");
        await WorkerCycleAsync();
        Assert.Contains("BAD-PARSE", _downloads);
        Assert.Equal((short)6, (await RowAsync("BAD-PARSE")).XmlFailedReads);
        _downloads.Clear();
        await WorkerCycleAsync();
        Assert.DoesNotContain("BAD-PARSE", _downloads);
        Assert.Equal(before, await SnapshotAsync());

        _downloads.Clear();
        _xml = _ => Xml("2026-11-20");
        await ManualSyncAsync(fullResync: true);

        Assert.Contains("BAD-PARSE", _downloads);
        var row = await RowAsync("BAD-PARSE");
        Assert.NotNull(row.XmlFetchedAt);
        Assert.Null(row.XmlFailedReads);
        Assert.Equal(new DateOnly(2026, 11, 20), row.DueDate);
        Assert.False(row.IsPaid);
        Assert.Null(row.PaidAt);
        Assert.Single(row.Lines);
    }

    [Fact]
    public async Task ThreeRowsThatAlwaysFailToDownload_DoNotStarveOlderRows()
    {
        for (var n = 1; n <= 3; n++)
            await InsertAsync($"BAD-{n}", daysAgo: n);
        await InsertAsync("GOOD", daysAgo: 10);
        await MigrateAsync();
        _xml = number => number.StartsWith("BAD", StringComparison.Ordinal) ? throw new HttpRequestException("500") : Xml("2026-11-20");

        await WorkerCycleAsync();

        Assert.Equal(["BAD-1", "BAD-2", "BAD-3"], _downloads);

        _downloads.Clear();
        await WorkerCycleAsync();

        Assert.Equal("GOOD", _downloads[0]);
        Assert.NotNull((await RowAsync("GOOD")).XmlFetchedAt);
    }

    [Fact]
    public async Task Fake429_DoesNotCountAsAFailedAttempt()
    {
        await InsertAsync("U-1");
        await MigrateAsync();
        _xml = _ => throw RateLimited(0);

        await WorkerCycleAsync();

        Assert.Null((await RowAsync("U-1")).XmlAttemptedAt);
    }

    [Fact]
    public async Task TruncatedReceivedResult_ContinuesFromLastRowAtPageZero()
    {
        await MigrateAsync();
        var queries = new List<KSeFQueryCriteria>();
        var lastRowAt = DateTime.UtcNow.AddHours(-3);
        _received = c =>
        {
            queries.Add(c);
            return queries.Count == 1
                ? Page([Bought("KSEF-T1")], lastRowAt, truncated: true)
                : Page([Bought("KSEF-T2")]);
        };

        var result = await ManualSyncAsync();

        Assert.Equal(TenantSyncOutcome.Success, result.Outcome);
        Assert.Equal(2, queries.Count);
        Assert.Equal(lastRowAt, queries[1].DateFrom);
        Assert.Equal(0, queries[1].PageOffset);
        Assert.Equal(2, result.NewInvoices);
    }

    [Fact]
    public async Task TruncatedResultThatDoesNotAdvance_FailsTheRun_WatermarkNotMoved()
    {
        await MigrateAsync();
        _received = c => Page([Bought("KSEF-T1")], c.DateFrom, truncated: true);

        var result = await ManualSyncAsync();

        Assert.Equal(TenantSyncOutcome.Failed, result.Outcome);
        await using var db = NewContext();
        Assert.Equal(LastSync, (await db.SyncStates.SingleAsync()).LastSuccessfulSync!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ReceivedStream_ResumesFromStoredHwm_WhenItIsOlderThanTheOverlap()
    {
        await MigrateAsync();
        var from = new List<DateTime?>();
        var hwm = DateTime.UtcNow.AddHours(-2);
        _received = c =>
        {
            from.Add(c.DateFrom);
            return Page([], hwm);
        };

        await ManualSyncAsync();
        await ManualSyncAsync();

        Assert.Equal(LastSync.AddHours(-1), from[0]!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(hwm, from[1]!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ReceivedStream_RecentHwm_KeepsTheOneHourOverlap()
    {
        await MigrateAsync();
        var from = new List<DateTime?>();
        _received = c =>
        {
            from.Add(c.DateFrom);
            return Page([], DateTime.UtcNow.AddMinutes(-1));
        };

        await ManualSyncAsync();
        var lastSuccessful = await LastSuccessfulSyncAsync();
        await ManualSyncAsync();

        Assert.Equal(lastSuccessful.AddHours(-1), from[1]!.Value, TimeSpan.FromMilliseconds(1));
    }

    private async Task<DateTime> LastSuccessfulSyncAsync()
    {
        await using var db = NewContext();
        return (await db.SyncStates.SingleAsync()).LastSuccessfulSync!.Value;
    }

    [Fact]
    public async Task FailedReceivedBatchOrTimeout_MarksOnlyThatTenantFailed_NextTenantStillSavesAndDownloads()
    {
        var timeoutTenant = Guid.NewGuid();
        var okTenant = Guid.NewGuid();
        await using (var db = NewContext())
        {
            await AddTenantAsync(db, timeoutTenant, "user-2", "5261040828");
            await AddTenantAsync(db, okTenant, "user-3", "7740001454");
        }

        await MigrateAsync();
        _received = c => c.SubjectNip switch
        {
            Nip => Page([Bought("KSEF-TOO-LONG") with { VendorName = new string('x', 600) }]),
            "5261040828" => throw new TaskCanceledException("HTTP timeout"),
            _ => Page([Bought("KSEF-OK")])
        };

        var results = await WorkerCycleAsync();

        Assert.Equal(TenantSyncOutcome.StoreFailed, results.Single(r => r.TenantId == _tenantId).Outcome);
        Assert.Equal(TenantSyncOutcome.Failed, results.Single(r => r.TenantId == timeoutTenant).Outcome);
        Assert.Equal(TenantSyncOutcome.Success, results.Single(r => r.TenantId == okTenant).Outcome);
        Assert.Equal(["KSEF-OK"], _downloads);
        Assert.Equal(new DateOnly(2026, 11, 20), (await RowAsync("KSEF-OK")).DueDate);

        await using (var db = NewContext())
            Assert.False(await db.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "KSEF-TOO-LONG"));
    }

    [Fact]
    public async Task RowThatFailsToSave_IsSkipped_TheRestOfTheQueueContinues()
    {
        await InsertAsync("BAD-ROW", daysAgo: 1, hasLine: false);
        await InsertAsync("GOOD-ROW", daysAgo: 2);
        await MigrateAsync();
        _xml = number => number == "BAD-ROW"
            ? Encoding.UTF8.GetBytes($"""<Faktura xmlns="{Fa3}"><Fa><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_8A>{new string('u', 300)}</P_8A></FaWiersz></Fa></Faktura>""")
            : Xml("2026-11-20");

        var results = await WorkerCycleAsync();

        Assert.Equal(TenantSyncOutcome.Success, Assert.Single(results).Outcome);
        Assert.Null((await RowAsync("BAD-ROW")).XmlFetchedAt);
        Assert.Equal(new DateOnly(2026, 11, 20), (await RowAsync("GOOD-ROW")).DueDate);
    }

    [Fact]
    public async Task SellerPaidWithoutDate_StoredAsMinValueOnPostgres_PayShowsNoQrAndNoDate()
    {
        await InsertAsync("PAID-NODATE", hasLine: false);
        await MigrateAsync();
        _xml = _ => Encoding.UTF8.GetBytes(
            $"""<Faktura xmlns="{Fa3}"><Fa><RodzajFaktury>VAT</RodzajFaktury><Platnosc><Zaplacono>1</Zaplacono><RachunekBankowy><NrRB>{Account}</NrRB></RachunekBankowy></Platnosc><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

        await WorkerCycleAsync();
        await using (var db = NewContext())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "InvoiceType" = 'Vat' WHERE "KSeFInvoiceNumber" = 'PAID-NODATE'""");

        var row = await RowAsync("PAID-NODATE");
        Assert.Equal(DateOnly.MinValue, row.SellerPaidOn);
        Assert.False(row.IsPaid);
        Assert.Null(row.PaidAt);
        var data = new TransferDetailsService().BuildTransferData(row, Nip);
        Assert.False(data.QrAvailable);
        Assert.Equal("Sprzedawca oznaczył fakturę jako zapłaconą.", data.QrMessage);
    }
}
