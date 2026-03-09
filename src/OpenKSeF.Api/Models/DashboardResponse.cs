namespace OpenKSeF.Api.Models;

public record TenantDashboardSummaryResponse(
    Guid TenantId,
    string Nip,
    string? DisplayName,
    DateTime? LastSyncedAt,
    DateTime? LastSuccessfulSync,
    int TotalInvoices,
    int InvoicesLast7Days,
    int InvoicesLast30Days,
    string SyncStatus);
