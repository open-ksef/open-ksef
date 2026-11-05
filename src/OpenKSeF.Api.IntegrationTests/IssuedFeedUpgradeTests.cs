using System.Net;
using KSeF.Client.Core.Exceptions;
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
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class IssuedFeedUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261103181145_AddInvoiceDueDate";
    private const string UserId = "user-1";
    private const string TenantNip = "1234563218";
    private const string PaidNumber = "KSEF-PAID";
    private const string UnpaidNumber = "KSEF-UNPAID";
    private const string Account = "PL61109010140000071219812874";

    private const string RollbackSql = """
        BEGIN;
        DELETE FROM "InvoiceHeaders" WHERE "Direction" = 'Issued';
        UPDATE "SyncStates" SET "IssuedContinuationAt" = NULL;
        COMMIT;
        """;

    private static readonly DateTime Issued = DateTime.UtcNow.AddDays(-10);
    private static readonly DateTime PaidAt = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastSync = DateTime.UtcNow.AddHours(-4);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ILogger<TenantSyncService> _logger = Substitute.For<ILogger<TenantSyncService>>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<KSeFQueryCriteria> _issuedQueries = [];

    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _received = _ => Page([]);
    private Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> _issued = c => Page([], c.DateTo);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var criteria = call.Arg<KSeFQueryCriteria>();
                if (criteria.Direction == InvoiceDirection.Received)
                    return _received(criteria);

                _issuedQueries.Add(criteria);
                return _issued(criteria);
            });
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private TenantSyncService SyncService(ApplicationDbContext db) => new(
        db,
        _gateway,
        Substitute.For<IEncryptionService>(),
        new InvoiceService(db, _notifications, NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { XmlDownloadSpacing = TimeSpan.Zero }),
        new XmlDownloadBlocks(),
        _logger);

    private List<string> Logged(LogLevel level, string text) => _logger.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)c.GetArguments()[0]! == level)
        .Select(c => c.GetArguments()[2]!.ToString()!)
        .Where(m => m.Contains(text))
        .ToList();

    private static KSeFInvoiceQueryResult Page(IReadOnlyList<KSeFInvoiceHeader> invoices, DateTime? continuationAt = null, bool truncated = false) =>
        new(invoices, invoices.Count, 0, 100, continuationAt, truncated);

    private static KSeFInvoiceHeader Sale(
        string number,
        string? buyerNip = "5261040828",
        string buyerIdType = "Nip",
        string? buyerForeignId = null,
        string vendorName = "Moja firma",
        string currency = "PLN") => new(
        number, number, "FV 7/2026", TenantNip, vendorName, buyerNip, "Nabywca", 200m, 46m, 246m, currency, Issued, Issued, "Vat",
        BuyerIdType: buyerIdType, BuyerForeignId: buyerForeignId);

    private static KSeFInvoiceHeader Bought(string number) => new(
        number, number, "FV 1/2026", "5261040828", "Dostawca", TenantNip, "Moja firma", 100m, 23m, 123m, "PLN", Issued, Issued, "Vat",
        BuyerIdType: "Nip");

    private async Task MigrateAndSeedExistingTenantAsync()
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = UserId,
            Nip = TenantNip,
            CreatedAt = now,
            UpdatedAt = now,
            KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
        });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncStates" ("Id", "TenantId", "LastSyncedAt", "LastSuccessfulSync")
            VALUES ({Guid.NewGuid()}, {_tenantId}, {LastSync}, {LastSync})
            """);

        var paidId = Guid.NewGuid();
        await InsertOldRowAsync(db, paidId, PaidNumber, true, PaidAt, new DateOnly(2026, 10, 15), Account);
        await InsertOldRowAsync(db, Guid.NewGuid(), UnpaidNumber, false, null, null, null);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceLines" ("Id", "InvoiceHeaderId", "LineNumber", "Name", "Quantity", "UnitPriceNet", "UnitPriceGross", "AmountNet", "AmountGross", "AmountVat")
            VALUES ({Guid.NewGuid()}, {paidId}, 1, 'Towar', 1, 100, 123, 100, 123, 23)
            """);

        await db.Database.MigrateAsync();
    }

    private Task InsertOldRowAsync(ApplicationDbContext db, Guid id, string number, bool isPaid, DateTime? paidAt, DateOnly? dueDate, string? account) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "InvoiceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt", "DueDate", "VendorBankAccount")
            VALUES ({id}, {_tenantId}, {number}, {number}, 'FV 1/2026', 'Dostawca', '5261040828', 100, 23, 123, {Issued}, {Issued}, {Issued}, {isPaid}, {paidAt}, {dueDate}, {account})
            """);

    private sealed record RowSnapshot(Guid Id, string Number, bool IsPaid, DateTime? PaidAt, DateOnly? DueDate, string? VendorBankAccount, int LineCount, string VendorName, decimal AmountGross, DateTime LastUpdatedAt);

    private static Task<List<RowSnapshot>> SnapshotReceivedAsync(ApplicationDbContext db) =>
        db.InvoiceHeaders.AsNoTracking()
            .Where(i => i.Direction == InvoiceDirection.Received)
            .OrderBy(i => i.KSeFInvoiceNumber)
            .Select(i => new RowSnapshot(i.Id, i.KSeFInvoiceNumber, i.IsPaid, i.PaidAt, i.DueDate, i.VendorBankAccount, i.Lines.Count, i.VendorName, i.AmountGross, i.LastUpdatedAt))
            .ToListAsync();

    [Fact]
    public async Task FirstIssuedRun_KeepsExistingRowsIdentical_Covers12Months_StoresSales_NoNotifications()
    {
        await MigrateAndSeedExistingTenantAsync();

        List<RowSnapshot> before;
        await using (var db = NewContext())
        {
            before = await SnapshotReceivedAsync(db);
            Assert.Equal(2, before.Count);
            Assert.Equal(1, before.Single(r => r.Number == PaidNumber).LineCount);
            Assert.True(before.Single(r => r.Number == PaidNumber).IsPaid);
        }

        _issued = c => Page(c.DateFrom < DateTime.UtcNow.AddMonths(-11)
            ? [Sale("KSEF-SALE-1"), Sale(PaidNumber, vendorName: "Nadpisane"), Sale(UnpaidNumber, vendorName: "Nadpisane")]
            : [], c.DateTo);

        await using (var db = NewContext())
        {
            var result = await SyncService(db).SyncTenantAsync(_tenantId, UserId);
            Assert.Equal(TenantSyncOutcome.Success, result.Outcome);
            Assert.Equal(0, result.NewInvoices);
        }

        await using (var db = NewContext())
        {
            Assert.Equal(before, await SnapshotReceivedAsync(db));

            var sale = await db.InvoiceHeaders.SingleAsync(i => i.Direction == InvoiceDirection.Issued);
            Assert.Equal("KSEF-SALE-1", sale.KSeFInvoiceNumber);
            Assert.False(sale.IsPaid);
            Assert.Null(sale.DueDate);
            Assert.Equal("Nip", sale.BuyerIdType);

            var state = await db.SyncStates.SingleAsync(s => s.TenantId == _tenantId);
            Assert.NotNull(state.IssuedContinuationAt);
            Assert.True(state.LastSuccessfulSync > LastSync);
        }

        var first = _issuedQueries[0];
        Assert.InRange(first.DateFrom!.Value, DateTime.UtcNow.AddMonths(-12).AddMinutes(-5), DateTime.UtcNow.AddMonths(-12).AddMinutes(5));
        Assert.InRange(_issuedQueries.Count, 4, 5);
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task ReceivedStream_StillStartsFromLastSuccessfulSync_AndNotifies()
    {
        await MigrateAndSeedExistingTenantAsync();
        var receivedFrom = new List<DateTime?>();
        _received = c =>
        {
            receivedFrom.Add(c.DateFrom);
            return Page([Bought("KSEF-NEW")]);
        };

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.Equal(LastSync.AddHours(-1), receivedFrom.Single()!.Value, TimeSpan.FromSeconds(1));
        await _notifications.Received(1).NotifyNewInvoiceAsync(Arg.Is<NewInvoiceDetectedEvent>(e => e.TenantId == _tenantId));
    }

    [Fact]
    public async Task IssuedStream429_ReceivedStoredAndNotified_ContinuationNotMoved_RunSucceeds()
    {
        await MigrateAndSeedExistingTenantAsync();
        _received = _ => Page([Bought("KSEF-NEW")]);
        _issued = _ => throw new KsefApiException("rate limited", HttpStatusCode.TooManyRequests);

        await using (var db = NewContext())
        {
            var result = await SyncService(db).SyncTenantAsync(_tenantId, UserId);
            Assert.Equal(TenantSyncOutcome.Success, result.Outcome);
            Assert.Equal(1, result.NewInvoices);
        }

        await using (var db = NewContext())
        {
            Assert.True(await db.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "KSEF-NEW" && i.Direction == InvoiceDirection.Received));
            Assert.Null((await db.SyncStates.SingleAsync()).IssuedContinuationAt);
        }

        await _notifications.Received(1).NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task FailedIssuedBatch_NextTenantStillSaves_ReceivedAndContinuation()
    {
        await MigrateAndSeedExistingTenantAsync();
        var secondTenantId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var now = DateTime.UtcNow;
            db.Tenants.Add(new Tenant
            {
                Id = secondTenantId,
                UserId = "user-2",
                Nip = "5261040828",
                CreatedAt = now,
                UpdatedAt = now,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = secondTenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
            });
            await db.SaveChangesAsync();
        }

        string? firstSubject = null;
        _issued = c =>
        {
            firstSubject ??= c.SubjectNip;
            return c.SubjectNip == firstSubject
                ? Page([Sale("KSEF-TOO-LONG", buyerNip: "9999999999", vendorName: new string('x', 600))], c.DateTo)
                : Page([Sale($"KSEF-OK-{c.SubjectNip}", buyerNip: "9999999999")], c.DateTo);
        };
        _received = c => Page([Bought($"KSEF-REC-{c.SubjectNip}")]);

        IReadOnlyList<TenantSyncResult> results;
        await using (var db = NewContext())
            results = await SyncService(db).SyncAllTenantsAsync();

        Assert.All(results, r => Assert.Equal(TenantSyncOutcome.Success, r.Outcome));
        Assert.Equal(2, results.Count);

        await using (var db = NewContext())
        {
            var failedTenantId = results.Single(r => r.Nip == firstSubject).TenantId;
            var okTenantId = results.Single(r => r.Nip != firstSubject).TenantId;
            var states = await db.SyncStates.ToDictionaryAsync(s => s.TenantId);

            Assert.Null(states[failedTenantId].IssuedContinuationAt);
            Assert.NotNull(states[okTenantId].IssuedContinuationAt);
            Assert.NotNull(states[okTenantId].LastSuccessfulSync);
            Assert.False(await db.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "KSEF-TOO-LONG"));
            Assert.True(await db.InvoiceHeaders.AnyAsync(i => i.TenantId == okTenantId && i.Direction == InvoiceDirection.Issued));
            Assert.Equal(2, await db.InvoiceHeaders.CountAsync(i => i.KSeFInvoiceNumber.StartsWith("KSEF-REC-")));
        }
    }

    [Fact]
    public async Task InterruptedFirstRun_ResumesFromLastFinishedWindow()
    {
        await MigrateAndSeedExistingTenantAsync();
        var failuresLeft = 1;
        DateTime? savedEnd = null;
        _issued = c =>
        {
            if (_issuedQueries.Count == 3 && failuresLeft-- > 0)
                throw new KsefApiException("rate limited", HttpStatusCode.TooManyRequests);
            if (_issuedQueries.Count == 2)
                savedEnd = c.DateTo;
            return Page([Sale($"KSEF-W{_issuedQueries.Count}")], c.DateTo);
        };

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync();
            Assert.Equal(savedEnd!.Value, state.IssuedContinuationAt!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Equal(2, await db.InvoiceHeaders.CountAsync(i => i.Direction == InvoiceDirection.Issued));
        }

        var firstRunQueries = _issuedQueries.Count;
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.Equal(savedEnd!.Value, _issuedQueries[firstRunQueries].DateFrom!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ContinuationThatDoesNotAdvance_StopsWithoutMoving()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = c => Page([], c.DateFrom);

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, UserId)).Outcome);

        Assert.Single(_issuedQueries);
        await using (var db = NewContext())
            Assert.Null((await db.SyncStates.SingleAsync()).IssuedContinuationAt);
    }

    [Fact]
    public async Task MissingHwm_StopsWithoutMoving_NoteStays()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = _ => Page([]);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.Single(_issuedQueries);
        Assert.NotEmpty(Logged(LogLevel.Warning, "did not advance"));
        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync();
            Assert.Null(state.IssuedContinuationAt);
            Assert.True(state.IssuedFetchIncomplete());
        }
    }

    [Fact]
    public async Task MissingHwmOnATruncatedPage_StopsWithoutMoving_NoteStays()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = _ => Page([], truncated: true);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.Single(_issuedQueries);
        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync();
            Assert.Null(state.IssuedContinuationAt);
            Assert.True(state.IssuedFetchIncomplete());
        }
    }

    [Fact]
    public async Task HwmPastTheWindow_IsClampedToWindowEnd_SoNoRangeIsSkipped()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = c => Page([], DateTime.UtcNow);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.InRange(_issuedQueries.Count, 4, 5);
        foreach (var (previous, next) in _issuedQueries.Zip(_issuedQueries.Skip(1)))
            Assert.Equal(previous.DateTo, next.DateFrom);
    }

    [Fact]
    public async Task TruncatedWindow_RestartsFromLastRow_AtPageZero()
    {
        await MigrateAndSeedExistingTenantAsync();
        var lastRowAt = DateTime.UtcNow.AddMonths(-11);
        var truncatedOnce = false;
        _issued = c =>
        {
            if (truncatedOnce)
                return Page([], c.DateTo);

            truncatedOnce = true;
            return new KSeFInvoiceQueryResult(
                Enumerable.Range(0, 100).Select(n => Sale($"KSEF-T{n}")).ToList(), 100, 0, 100, lastRowAt, true);
        };

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        Assert.Equal(lastRowAt, _issuedQueries[1].DateFrom);
        Assert.Equal(0, _issuedQueries[1].PageOffset);
    }

    private async Task SetContinuationAsync(DateTime continuationAt)
    {
        await using var db = NewContext();
        (await db.SyncStates.SingleAsync()).IssuedContinuationAt = continuationAt;
        await db.SaveChangesAsync();
    }

    private static Func<KSeFQueryCriteria, KSeFInvoiceQueryResult> KsefPages(IReadOnlyList<(DateTime At, string Number)> sales) => c =>
    {
        var rows = sales
            .Where(s => s.At >= c.DateFrom && s.At <= c.DateTo)
            .Skip(c.PageOffset * c.PageSize)
            .Take(c.PageSize)
            .ToList();
        return new KSeFInvoiceQueryResult(
            rows.Select(r => Sale(r.Number)).ToList(), rows.Count, c.PageOffset, c.PageSize, c.DateTo, false, rows.LastOrDefault().At);
    };

    [Fact]
    public async Task TwoThousandSalesInOneWindow_429AfterPage3_LaterRunsContinueFromSavedRow_AndFinish()
    {
        await MigrateAndSeedExistingTenantAsync();
        var start = DateTime.UtcNow.AddMonths(-5);
        var sales = Enumerable.Range(0, 2000).Select(n => (At: start.AddMinutes(n), Number: $"KSEF-{n:D4}")).ToList();
        var pages = KsefPages(sales);
        var servedRows = 0;
        var blocked = true;
        _issued = c =>
        {
            if (blocked && servedRows == 300)
                throw new KsefApiException("rate limited", HttpStatusCode.TooManyRequests);
            var page = pages(c);
            servedRows += page.Invoices.Count;
            return page;
        };

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, UserId)).Outcome);

        await using (var db = NewContext())
        {
            Assert.Equal(sales[299].At, (await db.SyncStates.SingleAsync()).IssuedContinuationAt!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Equal(300, await db.InvoiceHeaders.CountAsync(i => i.Direction == InvoiceDirection.Issued));
        }

        blocked = false;
        for (var run = 0; run < 2; run++)
        {
            var before = _issuedQueries.Count;
            await using var db = NewContext();
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
            Assert.InRange(_issuedQueries.Count - before, 1, 10);
        }

        await using (var db = NewContext())
        {
            var stored = await db.InvoiceHeaders.Where(i => i.Direction == InvoiceDirection.Issued).Select(i => i.KSeFInvoiceNumber).ToListAsync();
            Assert.Equal(sales.Select(s => s.Number), stored.Order());
            Assert.True((await db.SyncStates.SingleAsync()).IssuedContinuationAt > DateTime.UtcNow.AddMinutes(-15));
        }

        Assert.Empty(Logged(LogLevel.Warning, "stalled"));
    }

    [Fact]
    public async Task SecondPageFailsToStore_ContinuationStaysOnLastStoredRow_AndItsRowsAreAbsent()
    {
        await MigrateAndSeedExistingTenantAsync();
        var start = DateTime.UtcNow.AddMonths(-2);
        var at = (int n) => start.AddMinutes(n);
        _issued = c =>
        {
            var rows = Enumerable.Range(c.PageOffset * c.PageSize, c.PageSize).ToList();
            var invoices = rows
                .Select(n => Sale($"KSEF-{n:D3}", vendorName: n == 199 ? new string('x', 600) : "Moja firma"))
                .ToList();
            return new KSeFInvoiceQueryResult(invoices, invoices.Count, c.PageOffset, c.PageSize, c.DateTo, false, at(rows[^1]));
        };

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, UserId)).Outcome);

        Assert.Equal(2, _issuedQueries.Count);
        await using (var db = NewContext())
        {
            Assert.Equal(at(99), (await db.SyncStates.SingleAsync()).IssuedContinuationAt!.Value, TimeSpan.FromMilliseconds(1));
            var stored = await db.InvoiceHeaders.Where(i => i.Direction == InvoiceDirection.Issued).Select(i => i.KSeFInvoiceNumber).ToListAsync();
            Assert.Equal(Enumerable.Range(0, 100).Select(n => $"KSEF-{n:D3}"), stored.Order());
        }
    }

    [Fact]
    public async Task ThousandRowsOfOneTimestamp_WarnOnEachStalledRun_WithoutMovingTheContinuation()
    {
        await MigrateAndSeedExistingTenantAsync();
        var now = DateTime.UtcNow.AddDays(-5);
        var at = now.AddTicks(-(now.Ticks % 10));
        await SetContinuationAsync(at);
        _issued = KsefPages(Enumerable.Range(0, 1200).Select(n => (at, $"KSEF-{n:D4}")).ToList());

        for (var run = 1; run <= 2; run++)
        {
            await using var db = NewContext();
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
            Assert.Equal(run, Logged(LogLevel.Warning, "stalled").Count);
        }

        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync();
            Assert.Equal(at, state.IssuedContinuationAt);
            Assert.True(state.IssuedFetchIncomplete());
            Assert.Equal(1000, await db.InvoiceHeaders.CountAsync(i => i.Direction == InvoiceDirection.Issued));
        }
    }

    [Fact]
    public async Task IssuedFetchIncomplete_TrueAfter429_FalseOnceTheRunFinishes_AndWhileSkipped()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = _ => throw new KsefApiException("rate limited", HttpStatusCode.TooManyRequests);

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, UserId)).Outcome);
        await using (var db = NewContext())
            Assert.True((await db.SyncStates.SingleAsync()).IssuedFetchIncomplete());

        _issued = c => Page([], c.DateTo);
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
        await using (var db = NewContext())
            Assert.False((await db.SyncStates.SingleAsync()).IssuedFetchIncomplete());

        var queries = _issuedQueries.Count;
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
        Assert.Equal(queries, _issuedQueries.Count);
        await using (var db = NewContext())
            Assert.False((await db.SyncStates.SingleAsync()).IssuedFetchIncomplete());

        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync();
            state.IssuedContinuationAt = DateTime.UtcNow.AddHours(-4);
            await db.SaveChangesAsync();
        }
        _issued = _ => throw new KsefApiException("rate limited", HttpStatusCode.TooManyRequests);
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
        await using (var db = NewContext())
            Assert.True((await db.SyncStates.SingleAsync()).IssuedFetchIncomplete());
    }

    [Fact]
    public async Task FullPageOfOneTimestamp_AtTheResumePoint_PagesOnAndFinishes()
    {
        await MigrateAndSeedExistingTenantAsync();
        var at = DateTime.UtcNow.AddDays(-5);
        await SetContinuationAsync(at);
        _issued = KsefPages(Enumerable.Range(0, 150).Select(n => (at, $"KSEF-{n:D3}")).ToList());

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await using (var db = NewContext())
            Assert.Equal(150, await db.InvoiceHeaders.CountAsync(i => i.Direction == InvoiceDirection.Issued));
    }

    [Fact]
    public async Task IssuedStream_ContinuationWithin15Minutes_IsSkipped_OlderRuns()
    {
        await MigrateAndSeedExistingTenantAsync();
        await SetContinuationAsync(DateTime.UtcNow.AddMinutes(-14));

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await SyncService(db).SyncTenantAsync(_tenantId, UserId)).Outcome);
        Assert.Empty(_issuedQueries);
        Assert.Single(Logged(LogLevel.Information, "skipped"));

        await SetContinuationAsync(DateTime.UtcNow.AddMinutes(-16));
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
        Assert.Single(_issuedQueries);
        Assert.Single(Logged(LogLevel.Information, "skipped"));
    }

    [Fact]
    public async Task SelfInvoice_DroppedByIssuedStream_StoredAndNotifiedByReceived()
    {
        await MigrateAndSeedExistingTenantAsync();
        _received = _ => Page([Bought("KSEF-SELF")]);
        _issued = c => Page([Sale("KSEF-SELF", buyerNip: TenantNip), Sale("KSEF-ONLY-SALE-SELF", buyerNip: TenantNip)], c.DateTo);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await using (var db = NewContext())
        {
            Assert.Equal(InvoiceDirection.Received, (await db.InvoiceHeaders.SingleAsync(i => i.KSeFInvoiceNumber == "KSEF-SELF")).Direction);
            Assert.False(await db.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "KSEF-ONLY-SALE-SELF"));
        }

        await _notifications.Received(1).NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());
    }

    [Fact]
    public async Task ForeignBuyerWith50CharacterId_FitsTheBatch_InOwnCurrency()
    {
        await MigrateAndSeedExistingTenantAsync();
        var foreignId = new string('A', 50);
        _issued = c => Page(
            [
                Sale("KSEF-FOREIGN", buyerNip: null, buyerIdType: "Other", buyerForeignId: foreignId, currency: "GBP"),
                Sale("KSEF-NO-ID", buyerNip: null, buyerIdType: "None")
            ], c.DateTo);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await using (var db = NewContext())
        {
            var foreign = await db.InvoiceHeaders.SingleAsync(i => i.KSeFInvoiceNumber == "KSEF-FOREIGN");
            Assert.Null(foreign.BuyerNip);
            Assert.Equal("Other", foreign.BuyerIdType);
            Assert.Equal(foreignId, foreign.BuyerForeignId);
            Assert.Equal("GBP", foreign.Currency);
            Assert.Null((await db.InvoiceHeaders.SingleAsync(i => i.KSeFInvoiceNumber == "KSEF-NO-ID")).BuyerForeignId);
        }
    }

    [Fact]
    public async Task FullResync_StartsAtEarliestReceivedRow_NotAtAnIssuedOne()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = c => Page(
            c.DateFrom < DateTime.UtcNow.AddMonths(-11) ? [Sale("KSEF-OLD-SALE") with { IssueDate = DateTime.UtcNow.AddMonths(-11) }] : [],
            c.DateTo);
        var from = new List<DateTime?>();
        _received = c =>
        {
            from.Add(c.DateFrom);
            return Page([]);
        };

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);
        from.Clear();

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId, forceFullResync: true);

        Assert.Equal(Issued.AddHours(-1), from[0]!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RollbackSql_RemovesOnlyIssuedRows_OldInsertsGetTheDefault_NextRunRestoresThem()
    {
        await MigrateAndSeedExistingTenantAsync();
        _issued = c => Page(c.DateFrom < DateTime.UtcNow.AddMonths(-11) ? [Sale("KSEF-SALE-1")] : [], c.DateTo);

        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        List<RowSnapshot> before;
        await using (var db = NewContext())
        {
            before = await SnapshotReceivedAsync(db);
            var sale = await db.InvoiceHeaders.SingleAsync(i => i.Direction == InvoiceDirection.Issued);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "InvoiceLines" ("Id", "InvoiceHeaderId", "LineNumber", "Name", "Quantity", "UnitPriceNet", "UnitPriceGross", "AmountNet", "AmountGross", "AmountVat")
                VALUES ({Guid.NewGuid()}, {sale.Id}, 1, 'Usługa', 1, 200, 246, 200, 246, 46)
                """);

            await db.Database.ExecuteSqlRawAsync(RollbackSql);
        }

        await using (var db = NewContext())
        {
            Assert.Equal(before, await SnapshotReceivedAsync(db));
            Assert.False(await db.InvoiceHeaders.AnyAsync(i => i.Direction == InvoiceDirection.Issued));
            Assert.Equal(1, await db.InvoiceLines.CountAsync());
            Assert.Null((await db.SyncStates.SingleAsync()).IssuedContinuationAt);

            await InsertOldRowAsync(db, Guid.NewGuid(), "KSEF-OLD-IMAGE", false, null, null, null);
            Assert.Equal(InvoiceDirection.Received, (await db.InvoiceHeaders.SingleAsync(i => i.KSeFInvoiceNumber == "KSEF-OLD-IMAGE")).Direction);
        }

        _issuedQueries.Clear();
        await using (var db = NewContext())
            await SyncService(db).SyncTenantAsync(_tenantId, UserId);

        await using (var db = NewContext())
            Assert.True(await db.InvoiceHeaders.AnyAsync(i => i.KSeFInvoiceNumber == "KSEF-SALE-1" && i.Direction == InvoiceDirection.Issued));
    }
}
