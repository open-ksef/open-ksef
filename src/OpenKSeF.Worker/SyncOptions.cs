namespace OpenKSeF.Worker;

public class SyncOptions
{
    public const string SectionName = "Sync";

    public int IntervalHours { get; set; } = 4;
    public int BatchSize { get; set; } = 100;
}
