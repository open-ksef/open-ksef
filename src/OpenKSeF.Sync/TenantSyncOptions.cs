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
    /// Invoice XML downloads per manual sync, below the KSeF limit of 16 per minute. The rest waits for the Worker.
    /// </summary>
    public int XmlDownloadsPerManualRun { get; set; } = 15;

    /// <summary>
    /// Pause between manual XML downloads. KSeF allows 8 per second per NIP and IP; without a pause a real burst got a 429 at download 9.
    /// </summary>
    public TimeSpan XmlManualDownloadSpacing { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Pause between Worker XML downloads. KSeF allows 16 per minute per NIP and IP.
    /// </summary>
    public TimeSpan XmlDownloadSpacing { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long notifications keep sending after the sync is cancelled; fits Docker's 10 s stop window.
    /// </summary>
    public TimeSpan NotificationGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a manual sync waits for another run on the same tenant; covers a Worker XML pass of 60 downloads 4 s apart.
    /// </summary>
    public TimeSpan ManualLockWait { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a manual sync request without <c>Prefer: respond-async</c> waits for the result, below the mobile 30 s and nginx 60 s timeouts.
    /// </summary>
    public TimeSpan ManualSyncHold { get; set; } = TimeSpan.FromSeconds(25);
}
