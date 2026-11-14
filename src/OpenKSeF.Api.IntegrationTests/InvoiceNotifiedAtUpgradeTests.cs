using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class InvoiceNotifiedAtUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261114140113_AddAuditEvents";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Migration_BackfillsNotifiedAtFromFirstSeenAt_KeepsRowsAndPaidFlags()
    {
        var tenantId = Guid.NewGuid();
        var firstSeen = new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);
        var paidAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = firstSeen, UpdatedAt = firstSeen });
            await db.SaveChangesAsync();
            foreach (var (number, isPaid, paid) in new[] { ("KSEF-PAID", true, (DateTime?)paidAt), ("KSEF-UNPAID", false, null) })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt")
                    VALUES ({Guid.NewGuid()}, {tenantId}, {number}, {number}, 'Dostawca', '1111111111', 100, 23, 123, {firstSeen}, {firstSeen}, {firstSeen}, {isPaid}, {paid})
                    """);
            }

            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            var rows = (await db.InvoiceHeaders.AsNoTracking().ToListAsync()).ToDictionary(r => r.KSeFInvoiceNumber);
            Assert.Equal(2, rows.Count);
            Assert.All(rows.Values, r => Assert.Equal(r.FirstSeenAt, r.NotifiedAt));
            Assert.True(rows["KSEF-PAID"].IsPaid);
            Assert.Equal(paidAt, rows["KSEF-PAID"].PaidAt);
            Assert.False(rows["KSEF-UNPAID"].IsPaid);
            Assert.Null(rows["KSEF-UNPAID"].PaidAt);
        }
    }
}
