namespace EvCharging.ChargingWorker.Configuration;

public sealed class ReservationExpiryOptions
{
    public const string SectionName = "ReservationExpiry";

    /// <summary>Cron schedule of the overdue reservation scan.</summary>
    public string ScanSchedule { get; set; } = "";
    public int BatchSize { get; set; }
    public int MaximumConcurrentChecks { get; set; }
}
