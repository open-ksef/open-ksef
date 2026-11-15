using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Entities;

/// <summary>ADR-006 role matrix; permission strings are also the wire format.</summary>
public static class TenantPermissions
{
    public const string InvoicesRead = "invoices.read";
    public const string InvoicesMarkPaid = "invoices.markPaid";
    public const string SyncRun = "sync.run";
    public const string NotificationsSelf = "notifications.self";
    public const string IssuingDraft = "issuing.draft";
    public const string IssuingSubmit = "issuing.submit";
    public const string SyncFullResync = "sync.fullResync";
    public const string CredentialManage = "credential.manage";
    public const string TenantEdit = "tenant.edit";
    public const string MembersManage = "members.manage";
    public const string TenantDelete = "tenant.delete";

    private static readonly string[] EveryRole = [InvoicesRead, InvoicesMarkPaid, SyncRun, NotificationsSelf, IssuingDraft];
    private static readonly string[] Managers = [IssuingSubmit, SyncFullResync, CredentialManage, TenantEdit, MembersManage];

    private static readonly Dictionary<TenantRole, IReadOnlySet<string>> Matrix = new()
    {
        [TenantRole.Owner] = new HashSet<string>([.. EveryRole, .. Managers, TenantDelete]),
        [TenantRole.Administrator] = new HashSet<string>([.. EveryRole, .. Managers]),
        [TenantRole.Accountant] = new HashSet<string>([.. EveryRole, IssuingSubmit]),
        [TenantRole.Employee] = new HashSet<string>(EveryRole),
    };

    public static IReadOnlySet<string> For(TenantRole role) => Matrix[role];

    public static bool CanManageRole(TenantRole actor, TenantRole target) =>
        For(actor).Contains(MembersManage) && (actor == TenantRole.Owner || target != TenantRole.Owner);
}
