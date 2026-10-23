namespace EvCharging.ChargingWorker.Configuration;

public sealed class ReservationExpiryOptions
{
    public TimeSpan ScanInterval { get; set; }
    public int BatchSize { get; set; }
    public int MaximumConcurrentChecks { get; set; }
}
