using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>StoreCorrectionMode backfills WrongBuyerId only on corrections with the preset reason and writes nothing else.</summary>
public sealed class CorrectionModeUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261124075327_AddInvoiceTemplates";
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static Task InsertAsync(ApplicationDbContext db, Guid id, Guid tenantId, string type, Guid? correctedId, string? reason) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "IssuedInvoices" ("Id", "TenantId", "InvoiceType", "CorrectedInvoiceId", "CorrectionReason", "Status", "Number", "BuyerKind", "BuyerName", "Currency",
                "IssueDate", "SaleDate", "DueDate", "PaymentMethod", "SplitPayment", "SellerCashAccounting", "TotalNet", "TotalVat", "TotalGross", "AttemptCount", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {tenantId}, {type}, {correctedId}, {reason}, 'Draft', NULL, 'Company', 'Nabywca', 'PLN',
                {new DateOnly(2026, 11, 4)}, {new DateOnly(2026, 11, 4)}, {new DateOnly(2026, 11, 18)}, 'Transfer', false, false, -100, -23, -123, 0, {Now}, {Now})
            """);

    [Fact]
    public async Task Migration_BackfillsWrongBuyerIdOnPresetReasonCorrectionsOnly()
    {
        var tenantId = Guid.NewGuid();
        var (vat, wrongKor, wrongKorZal, otherKor) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
            await InsertAsync(db, vat, tenantId, "VAT", null, Corrections.WrongBuyerIdReason);
            await InsertAsync(db, wrongKor, tenantId, "KOR", vat, Corrections.WrongBuyerIdReason);
            await InsertAsync(db, wrongKorZal, tenantId, "KOR_ZAL", vat, Corrections.WrongBuyerIdReason);
            await InsertAsync(db, otherKor, tenantId, "KOR", vat, "Zwrot");
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var modes = await db.IssuedInvoices.AsNoTracking().ToDictionaryAsync(i => i.Id, i => (i.CorrectionMode, i.CorrectionReason));

            Assert.Equal(4, modes.Count);
            Assert.Equal(((CorrectionMode?)null, (string?)Corrections.WrongBuyerIdReason), modes[vat]);
            Assert.Equal(((CorrectionMode?)CorrectionMode.WrongBuyerId, (string?)Corrections.WrongBuyerIdReason), modes[wrongKor]);
            Assert.Equal(((CorrectionMode?)CorrectionMode.WrongBuyerId, (string?)Corrections.WrongBuyerIdReason), modes[wrongKorZal]);
            Assert.Equal(((CorrectionMode?)null, (string?)"Zwrot"), modes[otherKor]);
        }
    }
}
