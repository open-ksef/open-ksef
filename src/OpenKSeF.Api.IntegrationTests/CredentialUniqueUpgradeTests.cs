using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using Testcontainers.PostgreSql;

namespace OpenKSeF.Api.IntegrationTests;

public sealed class CredentialUniqueUpgradeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261109102546_InvoiceXmlFailedReads";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static Tenant Tenant(string userId) => new() { Id = Guid.NewGuid(), UserId = userId, Nip = "1111111111", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static KSeFCredential Credential(Guid tenantId, DateTime updatedAt, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        EncryptedToken = "encrypted",
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    [Fact]
    public async Task Migration_KeepsNewestCredentialPerTenant_ThenRejectsDuplicates_DownKeepsRows()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        var recent = DateTime.UtcNow.AddDays(-1);
        var duplicated = Tenant("user-a");
        var single = Tenant("user-b");
        var oldest = Credential(duplicated.Id, old);
        var tieLow = Credential(duplicated.Id, recent, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var tieHigh = Credential(duplicated.Id, recent, Guid.Parse("ffffffff-0000-0000-0000-000000000000"));
        var only = Credential(single.Id, old);

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            db.Tenants.AddRange(duplicated, single);
            db.KSeFCredentials.AddRange(oldest, tieLow, tieHigh, only);
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            var ids = await db.KSeFCredentials.Select(c => c.Id).ToListAsync();
            Assert.Equal(new[] { tieHigh.Id, only.Id }.Order(), ids.Order());

            db.KSeFCredentials.Add(Credential(single.Id, DateTime.UtcNow));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }

        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            Assert.Equal(2, await db.KSeFCredentials.CountAsync());
        }
    }
}
