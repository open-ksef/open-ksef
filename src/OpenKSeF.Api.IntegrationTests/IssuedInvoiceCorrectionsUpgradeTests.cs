using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>AddIssuedInvoiceCorrections leaves every pre-existing column and tuple of issuing tables byte-equal (ADR-007, M2).</summary>
public sealed class IssuedInvoiceCorrectionsUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261120120000_DropIssuedInvoiceSentXmlSha256";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static string Hash(string table, string newColumns) => $"""
        SELECT coalesce(md5(string_agg((to_jsonb(t) {newColumns})::text || ':' || t.xmin::text, ',' ORDER BY (to_jsonb(t) {newColumns})::text)), 'empty') AS "Value" FROM "{table}" t
        """;

    private static readonly (string Table, string NewColumns)[] Tables =
    [
        ("IssuedInvoices", "- 'InvoiceType' - 'CorrectedInvoiceId' - 'CorrectionReason' - 'CorrectionType' - 'OrderChainId' - 'AdvanceAmount' - 'CorrectionMode' - 'ProformaId'"),
        ("IssuedInvoiceLines", "- 'BaseLineNo'"),
        ("SellerProfiles", "- 'CorrectionNumberingPattern' - 'AdvanceNumberingPattern' - 'ProformaNumberingPattern'"),
        ("InvoiceNumberCounters", ""),
    ];

    private static async Task<List<string>> HashesAsync(ApplicationDbContext db, bool head)
    {
        var hashes = new List<string>();
        foreach (var (table, newColumns) in Tables)
            hashes.Add(await db.Database.SqlQueryRaw<string>(Hash(table, head ? newColumns : "")).SingleAsync());
        return hashes;
    }

    internal static async Task SeedAsync(ApplicationDbContext db, Guid tenantId)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SellerProfiles" ("TenantId", "LegalName", "AddressLine1", "BankAccount", "CashAccounting", "NumberingPattern")
            VALUES ({tenantId}, 'Sprzedawca', 'ul. Prosta 1', '93000000002026111700000815', false, {InvoiceNumbering.DefaultPattern})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceNumberCounters" ("TenantId", "Period", "Next") VALUES ({tenantId}, '2026-11', 4)
            """);

        var rows = new[]
        {
            (Id: Guid.NewGuid(), Status: "Draft", Number: (string?)null, Currency: "PLN", Kind: "Company", Ksef: (string?)null, Sent: (byte[]?)null, Upo: (byte[]?)null, Rate: VatRate.Rate23),
            (Id: Guid.NewGuid(), Status: "Accepted", Number: "FV/1/11/2026", Currency: "PLN", Kind: "Company", Ksef: "5213456789-20261103-ABCDEF123456-AB", Sent: new byte[] { 60, 70, 62 }, Upo: new byte[] { 60, 85, 62 }, Rate: VatRate.Rate23),
            (Id: Guid.NewGuid(), Status: "Rejected", Number: "FV/2/11/2026", Currency: "PLN", Kind: "Company", Ksef: null, Sent: new byte[] { 1 }, Upo: null, Rate: VatRate.Rate8),
            (Id: Guid.NewGuid(), Status: "Accepted", Number: "FV/3/11/2026", Currency: "GBP", Kind: "Foreign", Ksef: "5213456789-20261103-ABCDEF123456-CD", Sent: new byte[] { 2 }, Upo: new byte[] { 3 }, Rate: VatRate.NotTaxable),
        };
        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "IssuedInvoices" ("Id", "TenantId", "Status", "Number", "BuyerKind", "BuyerName", "Currency", "ExchangeRate", "IssueDate", "SaleDate", "DueDate",
                    "PaymentMethod", "SplitPayment", "SellerCashAccounting", "TotalNet", "TotalVat", "TotalGross", "KsefNumber", "SentXml", "UpoXml", "AttemptCount", "CreatedAt", "UpdatedAt")
                VALUES ({row.Id}, {tenantId}, {row.Status}, {row.Number}, {row.Kind}, 'Nabywca', {row.Currency}, {(row.Currency == "PLN" ? (decimal?)null : 5.1234m)},
                    {new DateOnly(2026, 11, 3)}, {new DateOnly(2026, 11, 3)}, {new DateOnly(2026, 11, 17)}, 'Transfer', false, false, 100, 23, 123, {row.Ksef}, {row.Sent}, {row.Upo}, 1, {Now}, {Now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "IssuedInvoiceLines" ("Id", "IssuedInvoiceId", "LineNo", "Name", "Unit", "Quantity", "UnitNetPrice", "VatRate", "NetAmount", "VatAmount")
                VALUES ({Guid.NewGuid()}, {row.Id}, 1, 'Usługa', 'szt.', 1, 100, {row.Rate.Code()}, 100, 23)
                """);
        }
    }

    [Fact]
    public async Task Migration_LeavesExistingRowsByteEqual_DefaultsVatAndKorPattern()
    {
        var tenantId = Guid.NewGuid();
        List<string> before;
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
            await SeedAsync(db, tenantId);
            before = await HashesAsync(db, head: false);
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            Assert.Equal(before, await HashesAsync(db, head: true));
            Assert.DoesNotContain("empty", before);

            var invoices = await db.IssuedInvoices.AsNoTracking().Include(i => i.Lines).ToListAsync();
            Assert.Equal(4, invoices.Count);
            Assert.All(invoices, i => Assert.Equal(IssuedInvoiceType.Vat, i.InvoiceType));
            Assert.All(invoices, i => Assert.Null(i.CorrectedInvoiceId ?? (object?)i.CorrectionReason ?? i.CorrectionType));
            Assert.All(invoices.SelectMany(i => i.Lines), l => Assert.Null(l.BaseLineNo));
            Assert.Equal(new byte[] { 60, 70, 62 }, invoices.Single(i => i.Number == "FV/1/11/2026").SentXml);
            var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
            Assert.Equal(("KOR/{nr}/{MM}/{RRRR}", "FV/{nr}/{MM}/{RRRR}"), (profile.CorrectionNumberingPattern, profile.NumberingPattern));
            Assert.Equal(0, await db.NumberSeriesCounters.CountAsync());
        }
    }
}
