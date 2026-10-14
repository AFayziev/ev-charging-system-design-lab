namespace EvCharging.NotificationWorker.Configuration;

public sealed class NotificationDeliveryOptions
{
    public string SenderEmail { get; init; } = "notifications@evcharging.test";
    public int MaximumConcurrentSends { get; init; } = 4;
    public TimeSpan[] RetryDelays { get; init; } = [];
}
