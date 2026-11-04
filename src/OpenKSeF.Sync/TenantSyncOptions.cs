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

    /// <summary>
    /// Invoice XML downloads per NIP per Worker cycle. KSeF allows 64 per hour per NIP and IP.
    /// </summary>
    public int XmlDownloadsPerCycle { get; set; } = 60;

    /// <summary>
    /// Pause between Worker XML downloads. KSeF allows 16 per minute per NIP and IP.
    /// </summary>
    public TimeSpan XmlDownloadSpacing { get; set; } = TimeSpan.FromSeconds(4);
}
