using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Tests;

public class TenantPermissionsTests
{
    private static readonly TenantRole[] Roles = [TenantRole.Owner, TenantRole.Administrator, TenantRole.Accountant, TenantRole.Employee];

    private static readonly Dictionary<string, string> Table = new()
    {
        ["invoices.read"] = "OAKE",
        ["invoices.markPaid"] = "OAKE",
        ["sync.run"] = "OAKE",
        ["notifications.self"] = "OAKE",
        ["issuing.draft"] = "OAKE",
        ["issuing.submit"] = "OAK-",
        ["invoices.export"] = "OAK-",
        ["sync.fullResync"] = "OA--",
        ["credential.manage"] = "OA--",
        ["tenant.edit"] = "OA--",
        ["members.manage"] = "OA--",
        ["tenant.delete"] = "O---",
    };

    public static TheoryData<TenantRole, string, bool> Cells()
    {
        var data = new TheoryData<TenantRole, string, bool>();
        foreach (var (permission, row) in Table)
            for (var i = 0; i < Roles.Length; i++)
                data.Add(Roles[i], permission, row[i] != '-');
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Matrix_Cell(TenantRole role, string permission, bool granted) =>
        Assert.Equal(granted, TenantPermissions.For(role).Contains(permission));

    [Fact]
    public void Matrix_Has48Cells_AndNoExtraPermissions()
    {
        Assert.Equal(48, Cells().Count);
        Assert.Equal(Roles, Enum.GetValues<TenantRole>());
        foreach (var role in Roles)
            Assert.Subset(Table.Keys.ToHashSet(), TenantPermissions.For(role).ToHashSet());
    }

    [Theory]
    [InlineData(TenantRole.Owner, TenantRole.Owner, true)]
    [InlineData(TenantRole.Owner, TenantRole.Administrator, true)]
    [InlineData(TenantRole.Owner, TenantRole.Accountant, true)]
    [InlineData(TenantRole.Owner, TenantRole.Employee, true)]
    [InlineData(TenantRole.Administrator, TenantRole.Owner, false)]
    [InlineData(TenantRole.Administrator, TenantRole.Administrator, true)]
    [InlineData(TenantRole.Administrator, TenantRole.Accountant, true)]
    [InlineData(TenantRole.Administrator, TenantRole.Employee, true)]
    [InlineData(TenantRole.Accountant, TenantRole.Owner, false)]
    [InlineData(TenantRole.Accountant, TenantRole.Administrator, false)]
    [InlineData(TenantRole.Accountant, TenantRole.Accountant, false)]
    [InlineData(TenantRole.Accountant, TenantRole.Employee, false)]
    [InlineData(TenantRole.Employee, TenantRole.Owner, false)]
    [InlineData(TenantRole.Employee, TenantRole.Administrator, false)]
    [InlineData(TenantRole.Employee, TenantRole.Accountant, false)]
    [InlineData(TenantRole.Employee, TenantRole.Employee, false)]
    public void CanManageRole(TenantRole actor, TenantRole target, bool allowed) =>
        Assert.Equal(allowed, TenantPermissions.CanManageRole(actor, target));
}
