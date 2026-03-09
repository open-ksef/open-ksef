namespace OpenKSeF.Sync;

public class TenantSyncOptions
{
    public const string SectionName = "Sync";

    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How many months back to query on the very first sync (no LastSuccessfulSync).
    /// KSeF limits each query to max 3 months, so the service iterates in windows.
    /// </summary>
    public int InitialSyncMonthsBack { get; set; } = 6;
}
