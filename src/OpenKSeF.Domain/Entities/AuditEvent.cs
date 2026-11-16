namespace OpenKSeF.Domain.Entities;

public class AuditEvent
{
    public const string TwoFactorReset = "TwoFactorReset";
    public const string TwoFactorResetPartial = "TwoFactorResetPartial";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoiceUnpaid = "invoice.unpaid";
    public const string CredentialSet = "credential.set";
    public const string CredentialDeleted = "credential.deleted";
    public const string MemberLeft = "member.left";
    public const string MemberRemoved = "member.removed";
    public const string MemberRoleChanged = "member.roleChanged";

    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? TenantId { get; set; }
    public required string ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorName { get; set; }
    public required string Action { get; set; }
    public string? TargetUserId { get; set; }
    public string? TargetEmail { get; set; }
    public string? Target { get; set; }
}
