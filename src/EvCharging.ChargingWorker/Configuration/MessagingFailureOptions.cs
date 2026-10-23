namespace EvCharging.ChargingWorker.Configuration;

public sealed class MessagingFailureOptions
{
    public const string SectionName = "MessagingFailures";

    public TimeSpan[] TransientDatabaseRetryDelays { get; set; } = [];
}
