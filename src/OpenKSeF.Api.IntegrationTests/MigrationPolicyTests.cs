using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>ADR-004 M2/M3: migrations after the cutoff may only expand the schema unless allow-listed with a reason.</summary>
public class MigrationPolicyTests
{
    private const string Cutoff = "20261102093230_AddIssuedInvoiceEscalatedAt";

    private static readonly Dictionary<string, string> AllowList = new()
    {
        ["20261106101846_AlignSyncColumnsWithKsef"] = "AlterColumn widens numeric(18,6)->numeric(22,6), Unit varchar(50)->256, VendorName/BuyerName varchar(500)->512 to match KSeF; no rewrite, no data loss. Down narrows and fails once wider values exist: roll back by image only.",
        ["20261106132321_DeviceTokenUniqueCascade"] = "Raw Sql deletes duplicate DeviceTokens rows (keeps newest per Token), then unique index on Token and FK re-created with cascade. Deleted rows are not restorable.",
    };

    [Fact]
    public void MigrationsAfterCutoff_AreAdditiveOrAllowListed()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused").Options);
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.Contains(Cutoff, assembly.Migrations.Keys);

        var failures = assembly.Migrations
            .Where(m => string.CompareOrdinal(m.Key, Cutoff) > 0)
            .SelectMany(m => Violations(m.Key, assembly.CreateMigration(m.Value, context.Database.ProviderName!).UpOperations, AllowList))
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void DropColumn_FailsUntilAllowListed() =>
        AssertFailsUntilAllowListed(new DropColumnOperation { Table = "InvoiceHeaders", Name = "VendorName" });

    [Fact]
    public void RawSql_FailsUntilAllowListed() =>
        AssertFailsUntilAllowListed(new SqlOperation { Sql = "UPDATE \"InvoiceHeaders\" SET \"IsPaid\" = true" });

    [Fact]
    public void NotNullColumnWithoutDefault_FailsUntilAllowListed() =>
        AssertFailsUntilAllowListed(new AddColumnOperation { Table = "InvoiceHeaders", Name = "Required", ClrType = typeof(string), IsNullable = false });

    [Fact]
    public void ExpandOperations_Pass()
    {
        var ops = new MigrationOperation[]
        {
            new CreateTableOperation { Name = "NewTable" },
            new AddPrimaryKeyOperation { Table = "NewTable", Name = "PK_NewTable", Columns = ["Id"] },
            new AddForeignKeyOperation { Table = "NewTable", Name = "FK_NewTable_Tenants", Columns = ["TenantId"], PrincipalTable = "Tenants" },
            new CreateIndexOperation { Table = "NewTable", Name = "IX_NewTable_Key", Columns = ["Key"], IsUnique = true },
            new CreateIndexOperation { Table = "InvoiceHeaders", Name = "IX_InvoiceHeaders_Key", Columns = ["Key"] },
            new AddColumnOperation { Table = "InvoiceHeaders", Name = "Optional", ClrType = typeof(string), IsNullable = true },
            new AddColumnOperation { Table = "InvoiceHeaders", Name = "Defaulted", ClrType = typeof(string), IsNullable = false, DefaultValue = "x" },
        };

        Assert.Empty(Violations("20991231000000_Expand", ops, new Dictionary<string, string>()));
    }

    [Fact]
    public void UniqueIndexOrForeignKeyOnExistingTable_Fails()
    {
        var ops = new MigrationOperation[]
        {
            new CreateIndexOperation { Table = "InvoiceHeaders", Name = "IX_Unique", Columns = ["Key"], IsUnique = true },
            new AddForeignKeyOperation { Table = "InvoiceHeaders", Name = "FK_New", Columns = ["TenantId"], PrincipalTable = "Tenants" },
        };

        Assert.Equal(2, Violations("20991231000000_Existing", ops, new Dictionary<string, string>()).Count);
    }

    private static void AssertFailsUntilAllowListed(MigrationOperation operation)
    {
        const string id = "20991231000000_Throwaway";
        Assert.Single(Violations(id, [operation], new Dictionary<string, string>()));
        Assert.Empty(Violations(id, [operation], new Dictionary<string, string> { [id] = "test" }));
    }

    private static List<string> Violations(string migrationId, IReadOnlyList<MigrationOperation> operations, IReadOnlyDictionary<string, string> allowList)
    {
        if (allowList.ContainsKey(migrationId))
            return [];

        var created = operations.OfType<CreateTableOperation>().Select(t => t.Name).ToHashSet();
        return operations
            .Where(op => !IsAdditive(op, created))
            .Select(op => $"{migrationId}: {op.GetType().Name} {Describe(op)} is non-additive (ADR-004 M3); allow-list it in {nameof(MigrationPolicyTests)} with a reason.")
            .ToList();
    }

    private static bool IsAdditive(MigrationOperation op, HashSet<string> created) => op switch
    {
        CreateTableOperation => true,
        AddColumnOperation c => c.IsNullable || c.DefaultValue is not null || c.DefaultValueSql is not null,
        CreateIndexOperation i => !i.IsUnique || created.Contains(i.Table),
        AddPrimaryKeyOperation k => created.Contains(k.Table),
        AddForeignKeyOperation f => created.Contains(f.Table),
        _ => false,
    };

    private static string Describe(MigrationOperation op) => op switch
    {
        ColumnOperation c => $"{c.Table}.{c.Name}",
        DropColumnOperation c => $"{c.Table}.{c.Name}",
        TableOperation t => t.Name,
        DropTableOperation t => t.Name,
        CreateIndexOperation i => $"{i.Table}.{i.Name}",
        DropForeignKeyOperation f => $"{f.Table}.{f.Name}",
        AddForeignKeyOperation f => $"{f.Table}.{f.Name}",
        SqlOperation => "(raw Sql)",
        _ => "",
    };
}
