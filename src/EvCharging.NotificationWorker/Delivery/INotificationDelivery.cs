using EvCharging.Persistence.Entities;

namespace EvCharging.NotificationWorker.Delivery;

public interface INotificationDelivery
{
    /// <summary>Provider name recorded on each delivery attempt.</summary>
    string Provider { get; }

    Task<NotificationDeliveryResult> SendAsync(
        Notification notification, Guid attemptId, CancellationToken cancellationToken);
}
