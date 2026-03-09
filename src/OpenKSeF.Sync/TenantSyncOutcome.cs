namespace OpenKSeF.Sync;

public enum TenantSyncOutcome
{
    Success = 0,
    TenantNotFound = 1,
    MissingCredential = 2,
    Failed = 3,
}
