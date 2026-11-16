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

    private const string RowHash = """SELECT md5(string_agg((to_jsonb(t) - 'PaidByUserId')::text, '|' ORDER BY t."Id")) AS "Value" FROM "InvoiceHeaders" t""";

    [Fact]
    public async Task Migration_AddsOneOwnerPerTenant_PaidByCreatorOnPaidRows_KeepsPaidFlagsAndDevices()
    {
        var created = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var tenants = new[] { (Id: Guid.NewGuid(), User: "user-a", Nip: "1111111111"), (Id: Guid.NewGuid(), User: "user-a", Nip: "2222222222"), (Id: Guid.NewGuid(), User: "user-b", Nip: "1111111111") };
        var paidAt = new Dictionary<string, DateTime?>();
        string before;

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

            before = await db.Database.SqlQueryRaw<string>(RowHash).SingleAsync();
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Equal(before, await db.Database.SqlQueryRaw<string>(RowHash).SingleAsync());
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

    [Fact]
    public async Task Rebackfill_RepairsRowsWrittenByRolledBackImage_AndIsIdempotent()
    {
        const string membershipMigration = "20261115144909_AddTenantMembership";
        var created = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var kept = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        string before;

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(membershipMigration);
            db.Tenants.Add(new Tenant { Id = kept, UserId = "user-a", Nip = "1111111111", CreatedAt = created, UpdatedAt = created });
            db.Tenants.Add(new Tenant { Id = orphan, UserId = "user-b", Nip = "1111111111", CreatedAt = created, UpdatedAt = created });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TenantMembers" ("TenantId", "UserId", "Role", "CreatedAt")
                VALUES ({kept}, 'user-a', 'Owner', {created}), ({kept}, 'user-e', 'Employee', {created})
                """);

            foreach (var (tenant, number, isPaid, paidBy) in new[]
            {
                (kept, "PAID-MARKED", true, "user-e"),
                (kept, "PAID-OLD-IMAGE", true, (string?)null),
                (kept, "UNPAID-STALE", false, "user-a"),
                (orphan, "ORPHAN-PAID", true, null),
                (orphan, "ORPHAN-UNPAID", false, null),
            })
            {
                DateTime? paidAt = isPaid ? created : null;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "InvoiceHeaders" ("Id", "TenantId", "KSeFInvoiceNumber", "KSeFReferenceNumber", "VendorName", "VendorNip", "AmountNet", "AmountVat", "AmountGross", "IssueDate", "FirstSeenAt", "LastUpdatedAt", "IsPaid", "PaidAt", "PaidByUserId")
                    VALUES ({Guid.NewGuid()}, {tenant}, {number}, {number}, 'Dostawca', '1111111111', 100, 23, 123, {created}, {created}, {created}, {isPaid}, {paidAt}, {paidBy})
                    """);
            }

            before = await db.Database.SqlQueryRaw<string>(RowHash).SingleAsync();
            await db.Database.MigrateAsync();
        }

        string afterFirst;
        await using (var db = NewContext())
        {
            Assert.Equal(before, await db.Database.SqlQueryRaw<string>(RowHash).SingleAsync());
            var paidBy = await db.InvoiceHeaders.AsNoTracking().ToDictionaryAsync(i => i.KSeFInvoiceNumber, i => i.PaidByUserId);
            Assert.Equal(5, paidBy.Count);
            Assert.Equal("user-e", paidBy["PAID-MARKED"]);
            Assert.Equal("user-a", paidBy["PAID-OLD-IMAGE"]);
            Assert.Null(paidBy["UNPAID-STALE"]);
            Assert.Equal("user-b", paidBy["ORPHAN-PAID"]);
            Assert.Null(paidBy["ORPHAN-UNPAID"]);

            var members = await db.TenantMembers.AsNoTracking().OrderBy(m => m.UserId).ToListAsync();
            Assert.Equal(["user-a", "user-b", "user-e"], members.Select(m => m.UserId));
            Assert.Equal([TenantRole.Owner, TenantRole.Owner, TenantRole.Employee], members.Select(m => m.Role));
            Assert.Equal(orphan, members[1].TenantId);
            Assert.All(members, m => Assert.True(m.NotifyNewInvoices));

            afterFirst = await StateHashAsync(db);
            await db.GetService<IMigrator>().MigrateAsync(membershipMigration);
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Equal(before, await db.Database.SqlQueryRaw<string>(RowHash).SingleAsync());
            Assert.Equal(afterFirst, await StateHashAsync(db));
        }
    }

    private static Task<string> StateHashAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT md5(
            (SELECT string_agg("TenantId"::text || "UserId" || "Role", '|' ORDER BY "TenantId", "UserId") FROM "TenantMembers") ||
            (SELECT string_agg("Id"::text || coalesce("PaidByUserId", '-'), '|' ORDER BY "Id") FROM "InvoiceHeaders")) AS "Value"
        """).SingleAsync();
}
