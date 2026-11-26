using Microsoft.EntityFrameworkCore;
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

/// <summary>REQ-0019 AC 3: a received invoice persisted with a past PermanentStorage date, visible only after a run, is stored by the next run.</summary>
public sealed class LatePermanentStorageTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<(KSeFInvoiceHeader Header, DateTime PermanentStorage, bool Visible)> _ksef = [];

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    // KSeF semantics (hwm.md): rows are filtered by PermanentStorage; HWM never passes a row that is not visible yet.
    private KSeFInvoiceQueryResult Query(KSeFQueryCriteria c)
    {
        var to = c.DateTo ?? DateTime.UtcNow;
        var hidden = _ksef.Where(r => !r.Visible).Select(r => r.PermanentStorage).DefaultIfEmpty(to).Min();
        var hwm = hidden < to ? hidden : to;
        var rows = c.Direction == InvoiceDirection.Issued
            ? []
            : _ksef.Where(r => r.Visible && r.PermanentStorage >= c.DateFrom && r.PermanentStorage <= to)
                .OrderBy(r => r.PermanentStorage)
                .Skip(c.PageOffset * c.PageSize).Take(c.PageSize)
                .Select(r => r.Header).ToList();
        return new KSeFInvoiceQueryResult(rows, hwm);
    }

    private static KSeFInvoiceHeader Bought(string number) => new(
        number, $"FV {number}", "7740001454", "Dostawca", "1111111111", "Moja firma", 100m, 23m, 123m, "PLN",
        DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), "Vat", BuyerIdType: "Nip");

    private TenantSyncService Sut(ApplicationDbContext db) => new(
        db,
        _gateway,
        new InvoiceService(db, Substitute.For<INotificationService>(), NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions { KSeFMetadataSpacing = TimeSpan.Zero, XmlDownloadSpacing = TimeSpan.Zero, XmlDownloadsPerManualRun = 0 }),
        new KSeFRequestLimits(TimeProvider.System),
        TimeProvider.System,
        NullLogger<TenantSyncService>.Instance);

    [Fact]
    public async Task ReceivedInvoice_PermanentStorage3hBeforeRunEnd_VisibleAfterIt_StoredOnceByNextRun_PaidUntouched()
    {
        var start = DateTime.UtcNow;
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            db.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                UserId = "user-1",
                Nip = "1111111111",
                CreatedAt = start,
                UpdatedAt = start,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = start, UpdatedAt = start }]
            });
            db.SyncStates.Add(new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                LastSyncedAt = start.AddHours(-6),
                LastSuccessfulSync = start.AddHours(-6),
                ReceivedContinuationAt = start.AddHours(-6),
                IssuedContinuationAt = start.AddHours(-6)
            });
            await db.SaveChangesAsync();
        }

        _ksef.Add((Bought("KSEF-EARLY"), start.AddHours(-5), true));
        _ksef.Add((Bought("KSEF-LATE"), start.AddHours(-3), false));
        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(call => Query(call.Arg<KSeFQueryCriteria>()));

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await Sut(db).SyncTenantAsync(_tenantId)).Outcome);

        DateTime firstRunEnd;
        await using (var db = NewContext())
        {
            var state = await db.SyncStates.SingleAsync(s => s.TenantId == _tenantId);
            firstRunEnd = state.LastSuccessfulSync!.Value;
            Assert.Equal(firstRunEnd.AddHours(-3), _ksef[1].PermanentStorage, TimeSpan.FromMinutes(1));
            var early = await db.InvoiceHeaders.SingleAsync(h => h.TenantId == _tenantId);
            Assert.Equal("KSEF-EARLY", early.KSeFInvoiceNumber);
            early.IsPaid = true;
            early.PaidAt = start;
            await db.SaveChangesAsync();
        }

        _ksef[1] = _ksef[1] with { Visible = true };

        await using (var db = NewContext())
            Assert.Equal(TenantSyncOutcome.Success, (await Sut(db).SyncTenantAsync(_tenantId)).Outcome);

        var receivedQueries = _gateway.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IKSeFGateway.QueryInvoicesAsync))
            .Select(c => (KSeFQueryCriteria)c.GetArguments()[1]!)
            .Where(q => q.Direction == InvoiceDirection.Received)
            .ToList();
        Assert.True(receivedQueries[^1].DateFrom <= start.AddHours(-3), $"Second run started at {receivedQueries[^1].DateFrom:O}, after the late row.");

        await using (var check = NewContext())
        {
            var rows = await check.InvoiceHeaders.AsNoTracking().Where(h => h.TenantId == _tenantId)
                .OrderBy(h => h.KSeFInvoiceNumber).ToListAsync();
            Assert.Equal(["KSEF-EARLY", "KSEF-LATE"], rows.Select(r => r.KSeFInvoiceNumber));
            Assert.True(rows[0].IsPaid);
            Assert.Equal(start, rows[0].PaidAt!.Value, TimeSpan.FromMilliseconds(1));
            Assert.False(rows[1].IsPaid);
        }
    }
}
