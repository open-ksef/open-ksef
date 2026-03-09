namespace OpenKSeF.Portal.Services;

public enum CredentialHealthStatus
{
    Active = 0,
    Warning = 1,
    Error = 2
}

public sealed class TenantCredentialStatusRow
{
    public required Guid TenantId { get; init; }
    public required string Nip { get; init; }
    public string? DisplayName { get; init; }
    public bool TokenConfigured { get; init; }
    public DateTime? LastSuccessfulSync { get; init; }
    public CredentialHealthStatus Status { get; init; }
}
