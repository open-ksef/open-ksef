using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>AddOrderChains leaves every pre-existing column and tuple of issuing tables byte-equal (ADR-007, M2).</summary>
public sealed class OrderChainsUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261120195706_AddIssuedInvoiceCorrections";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static readonly (string Table, string NewColumns)[] Tables =
    [
        ("IssuedInvoices", "- 'OrderChainId' - 'AdvanceAmount' - 'CorrectionMode' - 'ProformaId'"),
        ("IssuedInvoiceLines", ""),
        ("SellerProfiles", "- 'AdvanceNumberingPattern' - 'ProformaNumberingPattern'"),
        ("InvoiceNumberCounters", ""),
        ("NumberSeriesCounters", ""),
    ];

    private static async Task<List<string>> HashesAsync(ApplicationDbContext db, bool head)
    {
        var hashes = new List<string>();
        foreach (var (table, newColumns) in Tables)
        {
            var columns = head ? newColumns : "";
            hashes.Add(await db.Database.SqlQueryRaw<string>($"""
                SELECT coalesce(md5(string_agg((to_jsonb(t) {columns})::text || ':' || t.xmin::text, ',' ORDER BY (to_jsonb(t) {columns})::text)), 'empty') AS "Value" FROM "{table}" t
                """).SingleAsync());
        }

        return hashes;
    }

    [Fact]
    public async Task Migration_LeavesExistingRowsByteEqual_NoChainsAndZalPattern()
    {
        var tenantId = Guid.NewGuid();
        List<string> before;
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
            await IssuedInvoiceCorrectionsUpgradeTests.SeedAsync(db, tenantId);
            var original = await db.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "IssuedInvoices" WHERE "Number" = 'FV/1/11/2026'""").SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "IssuedInvoices" ("Id", "TenantId", "InvoiceType", "CorrectedInvoiceId", "CorrectionReason", "Status", "Number", "BuyerKind", "BuyerName", "Currency",
                    "IssueDate", "SaleDate", "DueDate", "PaymentMethod", "SplitPayment", "SellerCashAccounting", "TotalNet", "TotalVat", "TotalGross", "AttemptCount", "CreatedAt", "UpdatedAt")
                VALUES ({Guid.NewGuid()}, {tenantId}, 'KOR', {original}, 'Zwrot', 'Draft', NULL, 'Company', 'Nabywca', 'PLN',
                    {new DateOnly(2026, 11, 4)}, {new DateOnly(2026, 11, 4)}, {new DateOnly(2026, 11, 18)}, 'Transfer', false, false, -100, -23, -123, 0, {Now}, {Now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "NumberSeriesCounters" ("TenantId", "Series", "Period", "Next") VALUES ({tenantId}, 'KOR', '2026-11', 2)
                """);
            before = await HashesAsync(db, head: false);
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            Assert.Equal(before, await HashesAsync(db, head: true));
            Assert.DoesNotContain("empty", before);

            var invoices = await db.IssuedInvoices.AsNoTracking().ToListAsync();
            Assert.Equal(5, invoices.Count);
            Assert.All(invoices, i => Assert.Null(i.OrderChainId ?? (object?)i.AdvanceAmount));
            Assert.Equal(1, invoices.Count(i => i.InvoiceType == IssuedInvoiceType.Kor));
            Assert.Equal(new byte[] { 60, 70, 62 }, invoices.Single(i => i.Number == "FV/1/11/2026").SentXml);
            Assert.Equal(InvoiceNumbering.DefaultAdvancePattern, (await db.SellerProfiles.AsNoTracking().SingleAsync()).AdvanceNumberingPattern);
        }
    }
}
