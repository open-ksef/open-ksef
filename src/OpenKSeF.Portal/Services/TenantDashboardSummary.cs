namespace OpenKSeF.Portal.Services;

public enum SyncHealthStatus
{
    Success = 0,
    Warning = 1,
    Error = 2
}

public sealed class TenantDashboardSummary
{
    public required Guid TenantId { get; init; }
    public required string Nip { get; init; }
    public string? DisplayName { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public DateTime? LastSuccessfulSync { get; init; }
    public int TotalInvoices { get; init; }
    public int InvoicesLast7Days { get; init; }
    public int InvoicesLast30Days { get; init; }
    public SyncHealthStatus SyncStatus { get; init; }
}
