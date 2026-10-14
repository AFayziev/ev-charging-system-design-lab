using EvCharging.Persistence.Entities;

namespace EvCharging.NotificationWorker.Delivery;

public interface INotificationDelivery
{
    Task<NotificationDeliveryResult> SendAsync(
        Notification notification, Guid attemptId, CancellationToken cancellationToken);
}
