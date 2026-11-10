using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class SyncIsolationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261104210010_AddInvoiceXmlFetchedMarker";
    private const string NipA = "1234563218";
    private const string NipB = "5261040828";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly DateTime LastSync = DateTime.UtcNow.AddHours(-4);
    private static readonly DateTime ContinuationAt = DateTime.UtcNow.AddHours(-6);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly ILogger<InvoiceService> _invoiceLogger = Substitute.For<ILogger<InvoiceService>>();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _received = _ => Page([]);
    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _issued = c => Page([], c.DateTo);
    private Func<string, byte[]> _xml = _ => Xml("Towar nowy", "szt", "1", "100");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                return criteria.Direction == InvoiceDirection.Received ? _received(criteria) : _issued(criteria);
            });
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _xml(call.Arg<string>()));
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db) => new(
        db,
        _gateway,
        Substitute.For<IEncryptionService>(),
        new InvoiceService(db, Substitute.For<INotificationService>(), _invoiceLogger),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { BatchSize = 2, XmlDownloadSpacing = TimeSpan.Zero }),
        new XmlDownloadBlocks(),
        TimeProvider.System,
        NullLogger<TenantSyncService>.Instance);

    private async Task SeedAsync(string? migration = null)
    {
        await using var db = NewContext();
        if (migration is null)
            await db.Database.MigrateAsync();
        else
            await db.GetService<IMigrator>().MigrateAsync(migration);

        var now = DateTime.UtcNow;
        foreach (var (id, nip) in new[] { (_tenantA, NipA), (_tenantB, NipB) })
        {
            db.Tenants.Add(new Tenant
            {
                Id = id,
                UserId = $"user-{nip}",
                Nip = nip,
                CreatedAt = now,
                UpdatedAt = now,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = id, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
            });
        }
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync", "ReceivedContinuationAt")
            VALUES ({Guid.NewGuid()}, {_tenantA}, {LastSync}, {LastSync}, {ContinuationAt})
            """);
    }

    private static KSeFInvoiceQueryResult Page(IReadOnlyList<KSeFInvoiceHeader> invoices, DateTime? continuationAt = null) =>
        new(invoices, invoices.Count, 0, 2, continuationAt);

    private static KSeFInvoiceHeader Bought(string number, string buyerNip, string vendorName = "Dostawca", string buyerName = "Moja firma") => new(
        number, number, "FV 1/2026", "7740001454", vendorName, buyerNip, buyerName, 100m, 23m, 123m, "PLN", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, "Vat",
        BuyerIdType: "Nip");

    private static byte[] Xml(string name, string unit, string quantity, string unitPrice) => Encoding.UTF8.GetBytes(
        $"""<Faktura xmlns="{Fa3}"><Fa><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>{name}</P_7><P_8A>{unit}</P_8A><P_8B>{quantity}</P_8B><P_9A>{unitPrice}</P_9A><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

    private List<string> LoggedErrors() => _invoiceLogger.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)c.GetArguments()[0]! == LogLevel.Error)
        .Select(c => c.GetArguments()[2]!.ToString()!)
        .ToList();

    private async Task<SyncState?> StateAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await db.SyncStates.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId);
    }

    private async Task<List<string>> NumbersAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await db.InvoiceHeaders.Where(i => i.TenantId == tenantId).OrderBy(i => i.KSeFInvoiceNumber).Select(i => i.KSeFInvoiceNumber).ToListAsync();
    }

    [Fact]
    public async Task ReceivedPageFailsToStore_TenantFailedWithProgressKept_NextTenantStoresRowsStateAndXml()
    {
        await SeedAsync();
        _received = c => c.SubjectNip switch
        {
            NipA when c.PageOffset == 0 => Page([Bought("A-1", NipA), Bought("A-2", NipA)]),
            NipA when c.PageOffset == 1 => Page([Bought("A-3", NipA), Bought("A-BAD", NipA, vendorName: new string('x', 600))]),
            NipA => Page([]),
            _ => Page([Bought("B-1", NipB)])
        };

        await using var db = NewContext();
        var results = await SyncService(db).SyncAllTenantsAsync();

        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(TenantSyncOutcome.StoreFailed, results.Single(r => r.TenantId == _tenantA).Outcome);
        Assert.Contains("A-3, A-BAD", Assert.Single(LoggedErrors()));
        Assert.Equal(TenantSyncOutcome.Success, results.Single(r => r.TenantId == _tenantB).Outcome);

        Assert.Equal(["A-1", "A-2"], await NumbersAsync(_tenantA));
        var stateA = await StateAsync(_tenantA);
        Assert.Equal(LastSync, stateA!.LastSuccessfulSync!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(ContinuationAt, stateA.ReceivedContinuationAt!.Value, TimeSpan.FromMilliseconds(1));

        Assert.Equal(["B-1"], await NumbersAsync(_tenantB));
        Assert.NotNull((await StateAsync(_tenantB))?.LastSuccessfulSync);
        await using var check = NewContext();
        var b1 = await check.InvoiceHeaders.Include(i => i.Lines).SingleAsync(i => i.KSeFInvoiceNumber == "B-1");
        Assert.NotNull(b1.XmlFetchedAt);
        Assert.Equal("Towar nowy", Assert.Single(b1.Lines).Name);
    }

    [Fact]
    public async Task IssuedPageKeepsFailing_ReceivedXmlStillDownloads_IssuedIncompleteVisible()
    {
        await SeedAsync();
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1", NipA)]) : Page([]);
        _issued = c => c.SubjectNip == NipA
            ? Page([Bought("A-S-1", "9999999999"), Bought("A-S-BAD", "9999999999", vendorName: new string('x', 600))], c.DateTo)
            : Page([], c.DateTo);

        for (var run = 1; run <= 2; run++)
        {
            await using var db = NewContext();
            var a = (await SyncService(db).SyncAllTenantsAsync()).Single(r => r.TenantId == _tenantA);
            Assert.Equal(TenantSyncOutcome.StoreFailed, a.Outcome);
            Assert.NotNull(a.SyncedAtUtc);
        }

        await using var check = NewContext();
        var a1 = await check.InvoiceHeaders.Include(i => i.Lines).SingleAsync(i => i.KSeFInvoiceNumber == "A-1");
        Assert.NotNull(a1.XmlFetchedAt);
        Assert.Equal("Towar nowy", Assert.Single(a1.Lines).Name);
        Assert.Equal(["A-1"], await NumbersAsync(_tenantA));
        var state = await StateAsync(_tenantA);
        Assert.Null(state!.IssuedContinuationAt);
        Assert.True(state.IssuedFetchIncomplete());
        var errors = LoggedErrors();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, m => Assert.Contains("A-S-1, A-S-BAD", m));
    }

    [Fact]
    public async Task ManualSync_IssuedPageFails_ReceivedXmlStillDownloads_StoreFailed()
    {
        await SeedAsync();
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0 ? Page([Bought("A-1", NipA)]) : Page([]);
        _issued = c => Page([Bought("A-S-BAD", "9999999999", vendorName: new string('x', 600))], c.DateTo);

        await using (var db = NewContext())
        {
            var result = await SyncService(db).SyncTenantAsync(_tenantA);
            Assert.Equal(TenantSyncOutcome.StoreFailed, result.Outcome);
            Assert.Equal(0, result.QueuedDownloads + result.ErroredDownloads);
        }

        await using var check = NewContext();
        var a1 = await check.InvoiceHeaders.Include(i => i.Lines).SingleAsync(i => i.KSeFInvoiceNumber == "A-1");
        Assert.NotNull(a1.XmlFetchedAt);
        Assert.Equal("Towar nowy", Assert.Single(a1.Lines).Name);
        Assert.Equal(["A-1"], await NumbersAsync(_tenantA));
        Assert.Null((await StateAsync(_tenantA))!.IssuedContinuationAt);
    }

    [Fact]
    public async Task HttpTimeout_FailsOnlyThatTenant_CycleContinues()
    {
        await SeedAsync();
        _received = c => c.SubjectNip == NipA
            ? throw new TaskCanceledException("timeout", new TimeoutException())
            : Page([Bought("B-1", NipB)]);

        await using var db = NewContext();
        var results = await SyncService(db).SyncAllTenantsAsync();

        Assert.Equal(TenantSyncOutcome.Failed, results.Single(r => r.TenantId == _tenantA).Outcome);
        Assert.Equal(TenantSyncOutcome.Success, results.Single(r => r.TenantId == _tenantB).Outcome);
        Assert.Equal(["B-1"], await NumbersAsync(_tenantB));
    }

    [Fact]
    public async Task KsefMaximumNamesUnitAndQuantity_StoreUnchanged()
    {
        await SeedAsync();
        var vendor = new string('v', 512);
        var buyer = new string('b', 512);
        var unit = new string('u', 256);
        _received = c => c.SubjectNip == NipB ? Page([Bought("B-MAX", NipB, vendor, buyer)]) : Page([]);
        _xml = _ => Xml("Towar", unit, "1234567890123.123456", "9999999999999.999999");

        await using (var db = NewContext())
            Assert.All(await SyncService(db).SyncAllTenantsAsync(), r => Assert.Equal(TenantSyncOutcome.Success, r.Outcome));

        await using var check = NewContext();
        var row = await check.InvoiceHeaders.Include(i => i.Lines).SingleAsync(i => i.KSeFInvoiceNumber == "B-MAX");
        Assert.Equal((vendor, buyer), (row.VendorName, row.BuyerName));
        var line = Assert.Single(row.Lines);
        Assert.Equal(unit, line.Unit);
        Assert.Equal(1234567890123.123456m, line.Quantity);
        Assert.Equal(9999999999999.999999m, line.UnitPriceNet);
    }

    [Fact]
    public async Task SameKsefNumberTwiceInOnePage_StoredOnce_PageCommits_ProgressMoves()
    {
        await SeedAsync();
        _received = c => c.SubjectNip == NipA && c.PageOffset == 0
            ? Page([Bought("A-DUP", NipA), Bought("A-DUP", NipA)])
            : Page([]);

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantA)).Outcome);

        Assert.Equal(["A-DUP"], await NumbersAsync(_tenantA));
        Assert.True((await StateAsync(_tenantA))!.LastSuccessfulSync > LastSync);
    }

    [Fact]
    public async Task Migration_OnlyWidens_ExistingRowsAndPaidFlagsIdentical()
    {
        await SeedAsync(PreviousMigration);
        var paidId = Guid.NewGuid();
        var issued = DateTime.UtcNow.AddDays(-10);
        var paidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        const string RowsMd5 = """
            SELECT md5(coalesce((SELECT string_agg(h::text, '|' ORDER BY h."Id") FROM "InvoiceHeaders" h), '')
                || coalesce((SELECT string_agg(l::text, '|' ORDER BY l."Id") FROM "InvoiceLines" l), '')) AS "Value"
            """;
        const string PaidFlags = """SELECT string_agg("KSeFInvoiceNumber" || ':' || "IsPaid" || ':' || coalesce("PaidAt"::text, ''), '|' ORDER BY "KSeFInvoiceNumber") AS "Value" FROM "InvoiceHeaders" """;

        await using var db = NewContext();
        foreach (var (id, number, isPaid) in new[] { (paidId, "KSEF-PAID", true), (Guid.NewGuid(), "KSEF-UNPAID", false) })
        {
            DateTime? at = isPaid ? paidAt : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "BuyerName", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt")
                VALUES ({id}, {_tenantA}, {number}, {number}, {new string('v', 500)}, '7740001454', {new string('b', 500)}, 100, 23, 123, {issued}, {issued}, {issued}, {isPaid}, {at})
                """);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceLines" ("Id", "InvoiceHeaderId", "LineNumber", "Name", "Unit", "Quantity", "UnitPriceNet", "UnitPriceGross", "AmountNet", "AmountGross", "AmountVat")
            VALUES ({Guid.NewGuid()}, {paidId}, 1, 'Towar', {new string('u', 50)}, 999999999999.123456, 999999999999.123456, 123, 100, 123, 23)
            """);

        var rowsBefore = await db.Database.SqlQueryRaw<string>(RowsMd5).SingleAsync();
        var paidBefore = await db.Database.SqlQueryRaw<string>(PaidFlags).SingleAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261106101846_AlignSyncColumnsWithKsef");

        Assert.Equal(rowsBefore, await db.Database.SqlQueryRaw<string>(RowsMd5).SingleAsync());
        Assert.Equal(paidBefore, await db.Database.SqlQueryRaw<string>(PaidFlags).SingleAsync());
        Assert.StartsWith("KSEF-PAID:true:2026-10-01", paidBefore);
        Assert.EndsWith("|KSEF-UNPAID:false:", paidBefore);
    }
}
