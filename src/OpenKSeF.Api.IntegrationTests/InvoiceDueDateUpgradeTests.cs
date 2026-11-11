using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class InvoiceDueDateUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261102093230_AddIssuedInvoiceEscalatedAt";
    private const string UserId = "user-1";
    private const string PaidNumber = "KSEF-PAID";
    private const string UnpaidNumber = "KSEF-UNPAID";
    private const string FailingNumber = "KSEF-FAILS";
    private const string Account = "PL61109010140000071219812874";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly DateTime Issued = DateTime.UtcNow.AddDays(-10);
    private static readonly DateTime PaidAt = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task SeedBeforeMigrationAsync(ApplicationDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = UserId,
            Nip = "1234563218",
            CreatedAt = now,
            UpdatedAt = now,
            KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
        });
        await db.SaveChangesAsync();

        foreach (var (number, isPaid, paidAt) in new[] { (PaidNumber, true, (DateTime?)PaidAt), (UnpaidNumber, false, null), (FailingNumber, false, null) })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt", "VendorBankAccount")
                VALUES ({Guid.NewGuid()}, {_tenantId}, {number}, {number}, 'Dostawca', '5261040828', 100, 23, 123, {Issued}, {now}, {now}, {isPaid}, {paidAt}, {Account})
                """);
        }
    }

    private TenantSyncService SyncService(ApplicationDbContext db) => new(
        db,
        _gateway,
        new InvoiceService(db, _notifications, NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero }),
        new XmlDownloadBlocks(),
        TimeProvider.System,
        NullLogger<TenantSyncService>.Instance);

    private static byte[] XmlWithDueDate(string date) => Encoding.UTF8.GetBytes(
        $"""<Faktura xmlns="{Fa3}"><Fa><Platnosc><TerminPlatnosci><Termin>{date}</Termin></TerminPlatnosci><RachunekBankowy><NrRB>{Account}</NrRB></RachunekBankowy></Platnosc><FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>Towar</P_7><P_11>100</P_11><P_12>23</P_12></FaWiersz></Fa></Faktura>""");

    private static KSeFInvoiceHeader Header(string number) => new(
        number, "FV 1/2026", "5261040828", "Dostawca", null, null, 100m, 23m, 123m, "PLN", Issued, Issued, "Vat");

    [Fact]
    public async Task FullResync_FillsDatesOfPaidAndUnpaidRows_KeepsPaidMarks_ReportsFailedDownload()
    {
        await using (var db = NewContext())
        {
            await SeedBeforeMigrationAsync(db);
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            var rows = await db.InvoiceHeaders.ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Null(r.DueDate));
            var paid = rows.Single(r => r.KSeFInvoiceNumber == PaidNumber);
            Assert.True(paid.IsPaid);
            Assert.Equal(PaidAt, paid.PaidAt);
        }

        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new KSeFInvoiceQueryResult([Header(PaidNumber), Header(UnpaidNumber), Header(FailingNumber)]));
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), PaidNumber, Arg.Any<CancellationToken>()).Returns(XmlWithDueDate("2026-10-15"));
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), UnpaidNumber, Arg.Any<CancellationToken>()).Returns(XmlWithDueDate("2026-11-20"));
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), FailingNumber, Arg.Any<CancellationToken>())
            .Returns<Task<byte[]>>(_ => throw new InvalidOperationException("download failed"));

        await using (var db = NewContext())
        {
            var result = await SyncService(db).SyncTenantAsync(_tenantId, UserId, forceFullResync: true);
            Assert.Equal(TenantSyncOutcome.Success, result.Outcome);
            Assert.Equal(1, result.FailedDownloads);
        }

        await using (var db = NewContext())
        {
            var rows = (await db.InvoiceHeaders.ToListAsync()).ToDictionary(r => r.KSeFInvoiceNumber);
            Assert.Equal(new DateOnly(2026, 10, 15), rows[PaidNumber].DueDate);
            Assert.True(rows[PaidNumber].IsPaid);
            Assert.Equal(PaidAt, rows[PaidNumber].PaidAt);
            Assert.Equal(new DateOnly(2026, 11, 20), rows[UnpaidNumber].DueDate);
            Assert.False(rows[UnpaidNumber].IsPaid);
            Assert.Null(rows[FailingNumber].DueDate);
        }

        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task IncrementalSync_SkippedDownload_KeepsStoredDueDate()
    {
        await using (var db = NewContext())
        {
            await SeedBeforeMigrationAsync(db);
            await db.Database.MigrateAsync();
        }

        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new KSeFInvoiceQueryResult([Header(UnpaidNumber)]));
        _gateway.DownloadInvoiceAsync(Arg.Any<KSeFSession>(), UnpaidNumber, Arg.Any<CancellationToken>()).Returns(XmlWithDueDate("2026-11-20"));

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId, forceFullResync: true);
        _gateway.ClearReceivedCalls();

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await _gateway.DidNotReceive().DownloadInvoiceAsync(Arg.Any<KSeFSession>(), UnpaidNumber, Arg.Any<CancellationToken>());
        await using (var db = NewContext())
            Assert.Equal(new DateOnly(2026, 11, 20), (await db.InvoiceHeaders.SingleAsync(r => r.KSeFInvoiceNumber == UnpaidNumber)).DueDate);
    }

    [Fact]
    public async Task Rollback_DropsOnlyTheColumn()
    {
        await using (var db = NewContext())
        {
            await SeedBeforeMigrationAsync(db);
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("""UPDATE "InvoiceHeaders" SET "DueDate" = '2026-11-20'""");

            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            var count = await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "InvoiceHeaders" WHERE "IsPaid" AND "PaidAt" IS NOT NULL""").SingleAsync();
            var total = await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "InvoiceHeaders" """).SingleAsync();
            Assert.Equal((1, 3), (count, total));

            await db.Database.MigrateAsync();
            Assert.All(await db.InvoiceHeaders.ToListAsync(), r => Assert.Null(r.DueDate));
        }
    }
}
