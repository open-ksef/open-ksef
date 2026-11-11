using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class InvoiceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private readonly RecordingNotificationService _notifications = new();

    [Fact]
    public async Task UpsertInvoicesAsync_StoresWithoutNotifying()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);

        var newIds = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);

        Assert.Single(newIds);
        Assert.Equal(0, _notifications.Count);
        Assert.Equal("KSEF-1", (await _db.InvoiceHeaders.SingleAsync()).KSeFReferenceNumber);
    }

    [Theory]
    [InlineData(1, 1, null)]
    [InlineData(10, 10, null)]
    [InlineData(11, 10, 1)]
    [InlineData(25, 10, 15)]
    public async Task NotifyNewInvoicesAsync_NotifiesNewestTenByAcquisitionDate_SummarisesRest(int stored, int expectedSingles, int? expectedSummary)
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var ids = await sut.UpsertInvoicesAsync(tenantId, Enumerable.Range(0, stored)
            .Select(i => CreateInvoice() with { Number = $"KSEF-{i}", AcquisitionDate = start.AddHours(i) })
            .Reverse()
            .ToList());

        await sut.NotifyNewInvoicesAsync(tenantId, ids);

        Assert.Equal(stored, await _db.InvoiceHeaders.CountAsync());
        Assert.Equal(expectedSingles, _notifications.Count);
        var newest = await _db.InvoiceHeaders.OrderByDescending(i => i.AcquisitionDate).Take(expectedSingles).Select(i => i.Id).ToListAsync();
        Assert.Equal(newest.Order(), _notifications.InvoiceIds.Order());
        Assert.Equal(expectedSummary is null ? [] : [(tenantId, expectedSummary.Value)], _notifications.Summaries);
    }

    [Fact]
    public async Task ApplyInvoiceXmlAsync_FillsDueDate_UpsertKeepsIt_AndNeitherTouchesPaid()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var ids = await sut.UpsertInvoicesAsync(tenantId, [CreateInvoice()]);
        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        Assert.Null(stored.DueDate);
        stored.IsPaid = true;
        stored.PaidAt = paidAt;
        await _db.SaveChangesAsync();

        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails(null, [], new DateOnly(2026, 11, 20)));
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);
        Assert.NotNull(stored.XmlFetchedAt);

        await sut.UpsertInvoicesAsync(tenantId, [CreateInvoice()]);
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);
        Assert.True(stored.IsPaid);
        Assert.Equal(paidAt, stored.PaidAt);
    }

    [Fact]
    public async Task UpsertInvoicesAsync_SameNumberAgain_UpdatesRowInPlace_KeepsIdIssueDateAndPaid_NoNotification()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var first = CreateInvoice();
        var ids = await sut.UpsertInvoicesAsync(tenantId, [first]);
        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        var firstSeenAt = stored.FirstSeenAt;
        var paidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        stored.IsPaid = true;
        stored.PaidAt = paidAt;
        await _db.SaveChangesAsync();

        var newIds = await sut.UpsertInvoicesAsync(
            tenantId,
            [first with { InvoiceNumber = "FV/1/K", VendorName = "Vendor 2", AmountGross = 246m, Currency = null, IssueDate = first.IssueDate.AddDays(-3) }]);
        _db.ChangeTracker.Clear();

        Assert.Empty(newIds);
        var row = await _db.InvoiceHeaders.SingleAsync();
        Assert.Equal(ids[0], row.Id);
        Assert.Equal("FV/1/K", row.InvoiceNumber);
        Assert.Equal("Vendor 2", row.VendorName);
        Assert.Equal(246m, row.AmountGross);
        Assert.Equal("PLN", row.Currency);
        Assert.Equal(first.IssueDate, row.IssueDate);
        Assert.Equal(firstSeenAt, row.FirstSeenAt);
        Assert.True(row.IsPaid);
        Assert.Equal(paidAt, row.PaidAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NotifyNewInvoicesAsync_SyncCancelled_StopsNotifying_KeepsRows(int sentBeforeCancel)
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var ids = await sut.UpsertInvoicesAsync(tenantId, Enumerable.Range(0, 12).Select(i => CreateInvoice() with { Number = $"KSEF-{i}" }).ToList());
        using var cts = new CancellationTokenSource();
        if (sentBeforeCancel == 0)
            cts.Cancel();
        _notifications.AfterNotify = () => { if (_notifications.Count == sentBeforeCancel) cts.Cancel(); };

        await sut.NotifyNewInvoicesAsync(tenantId, ids, cts.Token);

        Assert.Equal(sentBeforeCancel, _notifications.Count);
        Assert.Empty(_notifications.Summaries);
        Assert.Equal(12, await _db.InvoiceHeaders.CountAsync(i => i.TenantId == tenantId && !i.IsPaid));
    }

    [Fact]
    public async Task NotifyNewInvoicesAsync_NotificationThrows_TriesEveryInvoiceAndSummary()
    {
        var throwing = new ThrowingNotificationService();
        var sut = new InvoiceService(_db, throwing, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var ids = await sut.UpsertInvoicesAsync(tenantId, Enumerable.Range(0, 12).Select(i => CreateInvoice() with { Number = $"KSEF-{i}" }).ToList());

        await sut.NotifyNewInvoicesAsync(tenantId, ids);

        Assert.Equal(11, throwing.Attempts);
        Assert.Equal(12, await _db.InvoiceHeaders.CountAsync());
    }

    [Fact]
    public async Task UpsertInvoicesAsync_PageOfExistingAndNew_LoadsExistingRowsInOneSelect()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var selects = new SelectCounter();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(selects)
            .Options);
        await db.Database.EnsureCreatedAsync();
        var sut = new InvoiceService(db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Entities.Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var existing = Enumerable.Range(0, 3).Select(i => CreateInvoice() with { Number = $"KSEF-{i}" }).ToList();
        await sut.UpsertInvoicesAsync(tenantId, existing);
        db.ChangeTracker.Clear();
        selects.Count = 0;

        var newIds = await sut.UpsertInvoicesAsync(tenantId, [.. existing.Select(i => i with { VendorName = "Vendor 2" }), CreateInvoice() with { Number = "KSEF-NEW" }]);

        Assert.Equal(1, selects.Count);
        Assert.Single(newIds);
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.InvoiceHeaders.CountAsync(i => i.TenantId == tenantId));
        Assert.Equal(3, await db.InvoiceHeaders.CountAsync(i => i.VendorName == "Vendor 2"));
    }

    [Fact]
    public async Task UpsertInvoicesAsync_OtherDirection_LeavesExistingRowUntouched()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var tenantId = Guid.NewGuid();
        var received = CreateInvoice();
        var ids = await sut.UpsertInvoicesAsync(tenantId, [received]);
        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        stored.DueDate = new DateOnly(2026, 11, 20);
        stored.VendorBankAccount = "PL61109010140000071219812874";
        stored.IsPaid = true;
        var updatedAt = stored.LastUpdatedAt;
        await _db.SaveChangesAsync();

        var newIds = await sut.UpsertInvoicesAsync(
            tenantId,
            [CreateInvoice() with { VendorName = "Nadpisane", AmountGross = 1m, Direction = InvoiceDirection.Issued, BuyerIdType = "Other" }]);

        Assert.Empty(newIds);
        Assert.Equal(InvoiceDirection.Received, stored.Direction);
        Assert.Equal(received.VendorName, stored.VendorName);
        Assert.Equal(received.AmountGross, stored.AmountGross);
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);
        Assert.Equal("PL61109010140000071219812874", stored.VendorBankAccount);
        Assert.Equal(updatedAt, stored.LastUpdatedAt);
        Assert.Null(stored.BuyerIdType);
        Assert.True(stored.IsPaid);
    }

    [Fact]
    public async Task ApplyInvoiceXmlAsync_IssuedRow_GetsLinesOnly()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice() with { Direction = InvoiceDirection.Issued }]);

        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails("PL61109010140000071219812874", [Line()], new DateOnly(2026, 11, 20)));

        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        Assert.Null(stored.DueDate);
        Assert.Null(stored.VendorBankAccount);
        Assert.NotNull(stored.XmlFetchedAt);
        Assert.Null(stored.Settlement);
        Assert.Equal(1, await _db.InvoiceLines.CountAsync(l => l.InvoiceHeaderId == ids[0]));
    }

    [Fact]
    public async Task ApplyInvoiceXmlAsync_KeepsStoredAccountAndLines()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);
        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails("PL61109010140000071219812874", [Line()], null));
        var lineId = (await _db.InvoiceLines.SingleAsync()).Id;

        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails("PL00000000000000000000000000", [Line(), Line()], new DateOnly(2026, 11, 20)));

        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        Assert.Equal("PL61109010140000071219812874", stored.VendorBankAccount);
        Assert.Equal(new DateOnly(2026, 11, 20), stored.DueDate);
        Assert.Equal(lineId, (await _db.InvoiceLines.SingleAsync()).Id);
    }

    [Fact]
    public async Task ApplyInvoiceXmlAsync_Reread_SetsPaymentFlags_ReplacesInvalidAccount_KeepsLinesAndPaid()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);
        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails("DE12000000000000000001", [Line()], null));
        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        stored.IsPaid = true;
        stored.PaidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        stored.Settlement = null;
        await _db.SaveChangesAsync();

        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails(
            "PL61109010140000071219812874", [Line(), Line()], null,
            SettlementState.Confirmed, 800m, true, new DateOnly(2026, 3, 5), 1,
            "PL94000000001234567890123456", "1111111111", "Faktor Testowy SA"));

        Assert.Equal("PL61109010140000071219812874", stored.VendorBankAccount);
        Assert.Equal(SettlementState.Confirmed, stored.Settlement);
        Assert.Equal(800m, stored.SettlementAmount);
        Assert.True(stored.SplitPayment);
        Assert.Equal(new DateOnly(2026, 3, 5), stored.SellerPaidOn);
        Assert.Equal((short)1, stored.PartialPayment);
        Assert.Equal("PL94000000001234567890123456", stored.FactorBankAccount);
        Assert.Equal("1111111111", stored.FactorNip);
        Assert.Equal("Faktor Testowy SA", stored.FactorName);
        Assert.True(stored.IsPaid);
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), stored.PaidAt);
        Assert.Equal(1, await _db.InvoiceLines.CountAsync(l => l.InvoiceHeaderId == ids[0]));
    }

    [Fact]
    public async Task RecordFailedInvoiceXmlAsync_KeepsKnownSettlement()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);
        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails(null, [], null, SettlementState.Overpaid, 300m));

        await sut.RecordFailedInvoiceXmlAsync(ids[0], xmlReceived: true);

        var stored = await _db.InvoiceHeaders.SingleAsync(i => i.Id == ids[0]);
        Assert.Equal(SettlementState.Overpaid, stored.Settlement);
        Assert.Equal(300m, stored.SettlementAmount);
    }

    [Fact]
    public async Task RecordFailedInvoiceXmlAsync_CountsOnlyReceivedXml_ResetOnRead()
    {
        var sut = new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);

        await sut.RecordFailedInvoiceXmlAsync(ids[0], xmlReceived: false);
        Assert.Null((await _db.InvoiceHeaders.AsNoTracking().SingleAsync(i => i.Id == ids[0])).XmlFailedReads);

        await sut.RecordFailedInvoiceXmlAsync(ids[0], xmlReceived: true);
        await sut.RecordFailedInvoiceXmlAsync(ids[0], xmlReceived: true);
        Assert.Equal((short)2, (await _db.InvoiceHeaders.AsNoTracking().SingleAsync(i => i.Id == ids[0])).XmlFailedReads);

        await sut.ApplyInvoiceXmlAsync(ids[0], new InvoiceXmlDetails(null, [], null));
        Assert.Null((await _db.InvoiceHeaders.AsNoTracking().SingleAsync(i => i.Id == ids[0])).XmlFailedReads);
    }

    [Fact]
    public async Task RecordFailedInvoiceXmlAsync_WarnsOnceWithKSeFNumber_WhenReadsReachCap()
    {
        var logger = Substitute.For<ILogger<InvoiceService>>();
        var sut = new InvoiceService(_db, _notifications, logger);
        var ids = await sut.UpsertInvoicesAsync(Guid.NewGuid(), [CreateInvoice()]);
        logger.ClearReceivedCalls();

        for (var i = 0; i < InvoiceService.MaxFailedXmlReads + 2; i++)
            await sut.RecordFailedInvoiceXmlAsync(ids[0], xmlReceived: true);

        var warning = Assert.Single(logger.ReceivedCalls(), c => c.GetArguments()[0] is LogLevel.Warning);
        Assert.Contains("KSEF-1", warning.GetArguments()[2]!.ToString());
    }

    private static InvoiceLineDto Line() => new(1, "Towar", "szt", 1m, 100m, 123m, 100m, 123m, 23m, "23");

    [Theory]
    [InlineData("2026-11-03", "2026-11-03T22:59:00Z", false, false)]
    [InlineData("2026-11-03", "2026-11-03T23:00:00Z", false, true)]
    [InlineData("2026-07-03", "2026-07-03T21:59:00Z", false, false)]
    [InlineData("2026-07-03", "2026-07-03T22:00:00Z", false, true)]
    [InlineData("2026-11-04", "2026-11-03T12:00:00Z", false, false)]
    [InlineData("2026-11-02", "2026-11-03T12:00:00Z", false, true)]
    [InlineData("2026-11-02", "2026-11-03T12:00:00Z", true, false)]
    [InlineData(null, "2026-11-03T12:00:00Z", false, false)]
    public void IsOverdue_UsesTodayInWarsaw_NotPaidNotUndated(string? due, string now, bool isPaid, bool expected)
    {
        var dueDate = due is null ? (DateOnly?)null : DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, Entities.InvoiceHeader.IsOverdue(isPaid, dueDate, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static InvoiceDto CreateInvoice() => new(
        Number: "KSEF-1",
        InvoiceNumber: "FV/1",
        VendorName: "Vendor",
        VendorNip: "1234567890",
        BuyerName: null,
        BuyerNip: null,
        AmountNet: 100m,
        AmountVat: 23m,
        AmountGross: 123m,
        Currency: "PLN",
        IssueDate: DateTime.UtcNow,
        AcquisitionDate: DateTime.UtcNow,
        InvoiceType: "VAT");

    public void Dispose() => _db.Dispose();

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Count++;
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<Guid> InvoiceIds { get; } = [];

        public List<(Guid TenantId, int Count)> Summaries { get; } = [];

        public int Count => InvoiceIds.Count;

        public Action? AfterNotify { get; set; }

        public Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt)
        {
            InvoiceIds.Add(evt.InvoiceId);
            AfterNotify?.Invoke();
            return Task.CompletedTask;
        }

        public Task NotifyNewInvoicesSummaryAsync(Guid tenantId, int count)
        {
            Summaries.Add((tenantId, count));
            return Task.CompletedTask;
        }

        public Task NotifyIssuedInvoiceStuckAsync(Guid tenantId, Guid issuedInvoiceId, string? number) => Task.CompletedTask;

        public Task SendConfirmationAsync(string deviceToken, Platform platform) => Task.CompletedTask;

        public Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform) => Task.FromResult(true);
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public int Attempts { get; private set; }

        public Task NotifyNewInvoiceAsync(NewInvoiceDetectedEvent evt)
        {
            Attempts++;
            throw new InvalidOperationException("push down");
        }

        public Task NotifyNewInvoicesSummaryAsync(Guid tenantId, int count)
        {
            Attempts++;
            throw new InvalidOperationException("push down");
        }

        public Task NotifyIssuedInvoiceStuckAsync(Guid tenantId, Guid issuedInvoiceId, string? number) => Task.CompletedTask;

        public Task SendConfirmationAsync(string deviceToken, Platform platform) => Task.CompletedTask;

        public Task<bool> SendTestNotificationAsync(string deviceToken, Platform platform) => Task.FromResult(true);
    }
}
