namespace OpenKSeF.Api.Models;

public record TenantSyncStatusResponse(
    Guid TenantId,
    string State,
    bool FullResync,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    TenantManualSyncResponse? Result,
    int? ErrorStatus,
    string? Error,
    DateTime? LastSuccessfulSyncUtc);
