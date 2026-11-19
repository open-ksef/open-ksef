using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class InvoiceUpsertTests(TestcontainersFixture fixture) : IAsyncLifetime
{
    private readonly string _database = $"upsert_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private string _connection = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {_database}", admin).ExecuteNonQueryAsync();
        }

        _connection = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString) { Database = _database }.ConnectionString;
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Tenants.Add(new Tenant { Id = _tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var own = new NpgsqlConnection(_connection))
            NpgsqlConnection.ClearPool(own);
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NewPage_StoresEveryRow_AndReturnsEveryId()
    {
        var page = Page(5);

        var ids = await UpsertAsync(page);

        Assert.Equal(5, ids.Count);
        await using var db = NewContext();
        Assert.Equal(
            page.Select(i => i.Number).Order(),
            (await db.InvoiceHeaders.Where(i => ids.Contains(i.Id)).Select(i => i.KSeFInvoiceNumber).ToListAsync()).Order());
        Assert.All(await db.InvoiceHeaders.ToListAsync(), i => Assert.Equal(i.KSeFInvoiceNumber, i.KSeFReferenceNumber));
    }

    [Fact]
    public async Task SamePageAgain_NoNewIds_RowsAndPaidFlagsKept()
    {
        var page = Page(5);
        var ids = await UpsertAsync(page);
        var paidAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        await using (var db = NewContext())
        {
            foreach (var row in await db.InvoiceHeaders.Where(i => i.KSeFInvoiceNumber == "KSEF-0" || i.KSeFInvoiceNumber == "KSEF-3").ToListAsync())
            {
                row.IsPaid = true;
                row.PaidAt = paidAt;
            }
            await db.SaveChangesAsync();
        }

        var again = await UpsertAsync(page);

        Assert.Empty(again);
        await using var check = NewContext();
        var rows = await check.InvoiceHeaders.OrderBy(i => i.KSeFInvoiceNumber).ToListAsync();
        Assert.Equal(ids.Order(), rows.Select(r => r.Id).Order());
        Assert.Equal(["KSEF-0", "KSEF-3"], rows.Where(r => r.IsPaid && r.PaidAt == paidAt).Select(r => r.KSeFInvoiceNumber));
    }

    [Fact]
    public async Task SameNumberTwiceInPage_StoresOneRow()
    {
        var invoice = Invoice("KSEF-DUP");

        var ids = await UpsertAsync([invoice, invoice with { VendorName = "Dostawca 2" }]);

        Assert.Single(ids);
        await using var db = NewContext();
        Assert.Equal(ids[0], (await db.InvoiceHeaders.SingleAsync()).Id);
    }

    [Fact]
    public async Task ExistingOtherDirectionRow_Untouched_NewRowsStored()
    {
        var issued = Invoice("KSEF-SHARED") with { Direction = InvoiceDirection.Issued };
        var issuedId = (await UpsertAsync([issued]))[0];

        var ids = await UpsertAsync([issued with { Direction = InvoiceDirection.Received, VendorName = "Dostawca 2" }, .. Page(3)]);

        Assert.Equal(3, ids.Count);
        Assert.DoesNotContain(issuedId, ids);
        await using var db = NewContext();
        Assert.Equal(4, await db.InvoiceHeaders.CountAsync());
        var existing = await db.InvoiceHeaders.SingleAsync(i => i.Id == issuedId);
        Assert.Equal(InvoiceDirection.Issued, existing.Direction);
        Assert.Equal("Dostawca", existing.VendorName);
    }

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connection).Options);

    private async Task<List<Guid>> UpsertAsync(IReadOnlyList<InvoiceDto> page)
    {
        await using var db = NewContext();
        return await new InvoiceService(db, Substitute.For<INotificationService>(), NullLogger<InvoiceService>.Instance)
            .UpsertInvoicesAsync(_tenantId, page);
    }

    private static List<InvoiceDto> Page(int count) => Enumerable.Range(0, count).Select(n => Invoice($"KSEF-{n}")).ToList();

    private static InvoiceDto Invoice(string number) => new(
        Number: number,
        InvoiceNumber: "FV 1/2026",
        VendorName: "Dostawca",
        VendorNip: "1111111111",
        BuyerName: "Moja firma",
        BuyerNip: "1111111111",
        AmountNet: 100m,
        AmountVat: 23m,
        AmountGross: 123m,
        Currency: "PLN",
        IssueDate: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        AcquisitionDate: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
        InvoiceType: "Vat");
}
