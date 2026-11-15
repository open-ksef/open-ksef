using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class TenantMembershipUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261115115136_AddInvoiceXmls";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Migration_AddsOneOwnerPerTenant_PaidByCreatorOnPaidRows_KeepsPaidFlagsAndDevices()
    {
        var created = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var tenants = new[] { (Id: Guid.NewGuid(), User: "user-a", Nip: "1111111111"), (Id: Guid.NewGuid(), User: "user-a", Nip: "2222222222"), (Id: Guid.NewGuid(), User: "user-b", Nip: "1111111111") };
        var paidAt = new Dictionary<string, DateTime?>();

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            foreach (var t in tenants)
                db.Tenants.Add(new Tenant { Id = t.Id, UserId = t.User, Nip = t.Nip, CreatedAt = created, UpdatedAt = created });
            await db.SaveChangesAsync();

            var n = 0;
            foreach (var t in tenants)
            {
                foreach (var isPaid in new[] { true, true, false })
                {
                    var number = $"KSEF-{n}";
                    DateTime? paid = isPaid ? created.AddMinutes(n) : null;
                    paidAt[number] = paid;
                    n++;
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt")
                        VALUES ({Guid.NewGuid()}, {t.Id}, {number}, {number}, 'Dostawca', '1111111111', 100, 23, 123, {created}, {created}, {created}, {isPaid}, {paid})
                        """);
                }
            }

            foreach (var (token, user, tenantId) in new[] { ("token-a", "user-a", (Guid?)tenants[0].Id), ("token-b", "user-b", null) })
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "DeviceTokens" ("Id", "UserId", "Token", "Platform", "TenantId", "CreatedAt", "UpdatedAt")
                    VALUES ({Guid.NewGuid()}, {user}, {token}, 0, {tenantId}, {created}, {created})
                    """);

            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            var members = await db.TenantMembers.AsNoTracking().ToListAsync();
            Assert.Equal(tenants.Length, members.Count);
            foreach (var t in tenants)
            {
                var owner = Assert.Single(members, m => m.TenantId == t.Id);
                Assert.Equal(t.User, owner.UserId);
                Assert.Equal(TenantRole.Owner, owner.Role);
                Assert.Equal(created, owner.CreatedAt);
            }

            var creators = tenants.ToDictionary(t => t.Id, t => t.User);
            var rows = await db.InvoiceHeaders.AsNoTracking().ToListAsync();
            Assert.Equal(9, rows.Count);
            Assert.Equal(6, rows.Count(r => r.IsPaid));
            foreach (var row in rows)
            {
                Assert.Equal(paidAt[row.KSeFInvoiceNumber], row.PaidAt);
                Assert.Equal(paidAt[row.KSeFInvoiceNumber] is not null, row.IsPaid);
                Assert.Equal(row.IsPaid ? creators[row.TenantId] : null, row.PaidByUserId);
            }

            var devices = await db.DeviceTokens.AsNoTracking().OrderBy(d => d.Token).ToListAsync();
            Assert.Equal(["token-a", "token-b"], devices.Select(d => d.Token));
            Assert.Equal(tenants[0].Id, devices[0].TenantId);
            Assert.Null(devices[1].TenantId);

            await db.Tenants.Where(t => t.Id == tenants[2].Id).ExecuteDeleteAsync();
            Assert.False(await db.TenantMembers.AnyAsync(m => m.TenantId == tenants[2].Id));
            Assert.Equal(2, await db.TenantMembers.CountAsync());
        }
    }
}
