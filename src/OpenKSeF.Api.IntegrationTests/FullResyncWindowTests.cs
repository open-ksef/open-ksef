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

public sealed class FullResyncWindowTests : IAsyncLifetime
{
    private const int KSeFMetadataQueriesPerHour = 20;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task FullResync_InvoiceIssuedYearsAgo_StartsAtAcquisitionDate_StaysWithinHourlyQueryLimit()
    {
        var now = DateTime.UtcNow;
        var acquired = now.AddDays(-10);
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            db.Tenants.Add(new Tenant
            {
                Id = _tenantId,
                UserId = "user-1",
                Nip = "1111111111",
                CreatedAt = now,
                UpdatedAt = now,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = _tenantId, EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now }]
            });
            db.InvoiceHeaders.Add(new InvoiceHeader
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                KSeFInvoiceNumber = "KSEF-OLD",
                KSeFReferenceNumber = "KSEF-OLD",
                VendorName = "Dostawca",
                VendorNip = "1111111111",
                Direction = InvoiceDirection.Received,
                IssueDate = new DateTime(2006, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AcquisitionDate = acquired,
                FirstSeenAt = now,
                LastUpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new KSeFInvoiceQueryResult([]));

        await using (var db = NewContext())
        {
            var sut = new TenantSyncService(
                db,
                _gateway,
                new InvoiceService(db, Substitute.For<INotificationService>(), NullLogger<InvoiceService>.Instance),
                new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
                Options.Create(new TenantSyncOptions { KSeFMetadataSpacing = TimeSpan.Zero, XmlDownloadSpacing = TimeSpan.Zero, XmlDownloadsPerManualRun = 0 }),
                new KSeFRequestLimits(),
                TimeProvider.System,
                NullLogger<TenantSyncService>.Instance);

            Assert.Equal(TenantSyncOutcome.Success, (await sut.SyncTenantAsync(_tenantId, forceFullResync: true)).Outcome);
        }

        var queries = _gateway.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IKSeFGateway.QueryInvoicesAsync))
            .Select(c => (KSeFQueryCriteria)c.GetArguments()[1]!)
            .ToList();
        Assert.InRange(queries.Count, 1, KSeFMetadataQueriesPerHour);
        var firstReceived = queries.First(q => q.Direction == InvoiceDirection.Received);
        Assert.Equal(acquired.AddHours(-1), firstReceived.DateFrom!.Value, TimeSpan.FromMilliseconds(1));
    }
}
