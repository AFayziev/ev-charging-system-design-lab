namespace EvCharging.NotificationWorker.Configuration;

public enum NotificationDeliveryProvider
{
    Mailpit,
    AmazonSes
}

public sealed class NotificationDeliveryOptions
{
    public NotificationDeliveryProvider Provider { get; init; } = NotificationDeliveryProvider.Mailpit;
    public string SenderEmail { get; init; } = "notifications@evcharging.test";
    public string SenderName { get; init; } = "EV Charging";
    public int MaximumConcurrentSends { get; init; } = 4;
    public TimeSpan[] RetryDelays { get; init; } = [];
}
