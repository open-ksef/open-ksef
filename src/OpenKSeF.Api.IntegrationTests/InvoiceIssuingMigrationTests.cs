using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class InvoiceIssuingMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260330210223_AddInvoiceLines";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static async Task<Guid> SeedSyncedDataAsync(ApplicationDbContext db)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), UserId = "user-1", Nip = "1234563218", CreatedAt = Now, UpdatedAt = Now };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var headerId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid")
            VALUES ({headerId}, {tenant.Id}, '5261040828-20261001-0100A0B0C0D0-E1', 'ref-1', 'Dostawca', '5261040828', 100, 23, 123, {Now}, {Now}, {Now}, false)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceLines" ("Id", "InvoiceHeaderId", "LineNumber", "Name", "VatRate", "AmountNet")
            VALUES ({Guid.NewGuid()}, {headerId}, 1, 'Towar', '23', 100)
            """);
        return tenant.Id;
    }

    private static async Task AssertSyncedDataIntactAsync(ApplicationDbContext db)
    {
        Assert.Equal("5261040828-20261001-0100A0B0C0D0-E1", await db.Database.SqlQueryRaw<string>("""SELECT "KSeFInvoiceNumber" AS "Value" FROM "InvoiceHeaders" """).SingleAsync());
        Assert.Equal(123m, await db.Database.SqlQueryRaw<decimal>("""SELECT "AmountGross" AS "Value" FROM "InvoiceHeaders" """).SingleAsync());
        Assert.Equal("Towar 23 100.00", await db.Database.SqlQueryRaw<string>("""SELECT concat_ws(' ', "Name", "VatRate", "AmountNet") AS "Value" FROM "InvoiceLines" """).SingleAsync());
        Assert.Equal(1, await db.Tenants.CountAsync());
    }

    private static IssuedInvoice NewInvoice(Guid tenantId)
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), tenantId, new DateOnly(2026, 11, 3), Now);
        invoice.SetBuyer(BuyerKind.Company, "5261040828", "Nabywca", "ul. Długa 2", "00-002 Warszawa");
        invoice.ReplaceLines([("Usługa", null, 1.5m, 33.33m, VatRate.Rate23), ("Szkolenie", "h", 2, 100m, VatRate.Exempt), ("Eksport", null, 1, 5m, VatRate.ZeroDomestic)]);
        invoice.SetExemptionBasis(ExemptionBasisKind.Act, "art. 43 ust. 1 pkt 29 ustawy o VAT");
        return invoice;
    }

    [Fact]
    public async Task Migration_IsAdditive_KeepsSyncedData_AndRoundTripsIssuing()
    {
        Guid tenantId;
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            tenantId = await SeedSyncedDataAsync(db);
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            await AssertSyncedDataIntactAsync(db);

            var profile = new SellerProfile(tenantId);
            profile.Update("Sprzedawca", "ul. Prosta 1", null, "61 1090 1014 0000 0712 1981 2874", false, InvoiceNumbering.DefaultPattern);
            db.SellerProfiles.Add(profile);
            db.IssuedInvoices.Add(NewInvoice(tenantId));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var invoice = await db.IssuedInvoices.Include(i => i.Lines).SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 3), invoice.IssueDate);
            Assert.Equal(new DateOnly(2026, 11, 17), invoice.DueDate);
            Assert.Equal([VatRate.Rate23, VatRate.Exempt, VatRate.ZeroDomestic], invoice.Lines.OrderBy(l => l.LineNo).Select(l => l.VatRate));
            Assert.Equal(1.5m, invoice.Lines.Single(l => l.LineNo == 1).Quantity);
            Assert.Equal((255m, 11.50m, 266.50m), (invoice.TotalNet, invoice.TotalVat, invoice.TotalGross));
            Assert.Equal("61109010140000071219812874", (await db.SellerProfiles.SingleAsync()).BankAccount);

            invoice.ReplaceLines([("Nowa", null, 1, 10m, VatRate.Rate8)]);
            invoice.AddLine("Druga", null, 1, 10m, VatRate.Rate8);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Equal(["Nowa", "Druga"], await db.IssuedInvoiceLines.OrderBy(l => l.LineNo).Select(l => l.Name).ToListAsync());
            Assert.Equal(["8", "8"], await db.Database.SqlQueryRaw<string>("SELECT \"VatRate\" AS \"Value\" FROM \"IssuedInvoiceLines\"").ToListAsync());
        }
    }

    [Fact]
    public async Task IssuedInvoice_UsesXminConcurrency()
    {
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var tenantId = await SeedSyncedDataAsync(db);
            db.IssuedInvoices.Add(NewInvoice(tenantId));
            await db.SaveChangesAsync();
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.IssuedInvoices.SingleAsync();
        var b = await second.IssuedInvoices.SingleAsync();

        a.SetPayment(PaymentMethod.Cash, false);
        await first.SaveChangesAsync();
        b.SetPayment(PaymentMethod.Card, false);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Number_IsUniquePerTenant_AndCounterUpsertIsAtomic()
    {
        Guid tenantId;
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            tenantId = await SeedSyncedDataAsync(db);
            db.IssuedInvoices.AddRange(NewInvoice(tenantId), NewInvoice(tenantId));
            await db.SaveChangesAsync();
        }

        var profile = new SellerProfile(tenantId);
        profile.Update("Sprzedawca", "ul. Prosta 1", null, "61109010140000071219812874", false, InvoiceNumbering.DefaultPattern);
        await using (var db = NewContext())
        {
            var invoices = await db.IssuedInvoices.Include(i => i.Lines).ToListAsync();
            foreach (var invoice in invoices)
                invoice.Issue(profile, "1234563218", "FV/1/11/2026", Now);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = NewContext())
        {
            const string next = """
                INSERT INTO "InvoiceNumberCounters" ("TenantId", "Period", "Next") VALUES ({0}, {1}, 2)
                ON CONFLICT ("TenantId", "Period") DO UPDATE SET "Next" = "InvoiceNumberCounters"."Next" + 1
                RETURNING "Next" - 1 AS "Value"
                """;
            var numbers = new List<int>();
            for (var i = 0; i < 3; i++)
                numbers.Add((await db.Database.SqlQueryRaw<int>(next, tenantId, "2026-11").ToListAsync()).Single());
            Assert.Equal([1, 2, 3], numbers);
        }
    }

    [Fact]
    public async Task Down_DropsOnlyIssuingTables()
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await SeedSyncedDataAsync(db);
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        await AssertSyncedDataIntactAsync(db);
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'").ToListAsync();
        Assert.DoesNotContain("IssuedInvoices", tables);
        Assert.DoesNotContain("IssuedInvoiceLines", tables);
        Assert.DoesNotContain("SellerProfiles", tables);
        Assert.DoesNotContain("InvoiceNumberCounters", tables);
        Assert.Contains("InvoiceHeaders", tables);
    }
}
