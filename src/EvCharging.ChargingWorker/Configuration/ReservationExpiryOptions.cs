namespace EvCharging.ChargingWorker.Configuration;

public sealed class ReservationExpiryOptions
{
    public TimeSpan SweepInterval { get; set; }
    public int SweepBatchSize { get; set; }
    public int MaximumConcurrentChecks { get; set; }
}
