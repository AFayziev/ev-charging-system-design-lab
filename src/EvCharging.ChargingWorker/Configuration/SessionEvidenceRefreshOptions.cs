namespace EvCharging.ChargingWorker.Configuration;

public sealed class SessionEvidenceRefreshOptions
{
    public const string SectionName = "SessionEvidenceRefresh";

    /// <summary>Cron schedule of the stale session scan.</summary>
    public string ScanSchedule { get; set; } = "";

    /// <summary>Minimum gap between provider reads of a stale session.</summary>
    public TimeSpan MinimumReadInterval { get; set; }

    /// <summary>Maximum gap between provider reads of a stale session.</summary>
    public TimeSpan MaximumReadInterval { get; set; }
    public int BatchSize { get; set; }
    public int MaximumConcurrentSnapshotReads { get; set; }
}
