namespace OpenKSeF.Domain.Entities;

public class AuditEvent
{
    public const string TwoFactorReset = "TwoFactorReset";

    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public required string ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public required string Action { get; set; }
    public required string TargetUserId { get; set; }
    public string? TargetEmail { get; set; }
}
