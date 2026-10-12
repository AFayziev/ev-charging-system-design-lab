namespace EvCharging.ChargingWorker.Configuration;

public sealed class MessagingFailureOptions
{
    public TimeSpan[] TransientDatabaseRetryDelays { get; set; } = [];
}
