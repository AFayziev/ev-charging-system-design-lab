namespace EvCharging.NotificationWorker.Configuration;

internal sealed class MessagingFailureOptions
{
    public TimeSpan[] TransientDatabaseRetryDelays { get; set; } = [];
}
