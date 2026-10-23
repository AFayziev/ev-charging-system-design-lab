using EvCharging.NotificationWorker.Delivery;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.NotificationWorker.Handlers;

public static class SendNotificationMessageHandler
{
    [NonTransactional]
    public static async Task HandleAsync(
        SendNotificationMessage message,
        PlatformDbContext database,
        INotificationDelivery delivery,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var notification = await database.Notifications.SingleOrDefaultAsync(
            value => value.Id == message.NotificationId, cancellationToken);
        if (notification is null)
        {
            return;
        }

        var previous = await database.NotificationDeliveryAttempts.AsNoTracking()
            .Where(value => value.NotificationId == message.NotificationId)
            .OrderByDescending(value => value.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous is not null && (previous.Outcome != NotificationDeliveryOutcome.NotCaptured
            || previous.CompletedAt is null))
        {
            return;
        }

        var attempt = NotificationDeliveryAttempt.Start(notification, (previous?.Number ?? 0) + 1, clock.GetUtcNow());
        database.NotificationDeliveryAttempts.Add(attempt);
        // Commit before HTTP. The unique notification/number pair excludes competing sends;
        // process loss leaves uncertainty that prevents redelivery from repeating the effect.
        await database.SaveChangesAsync(cancellationToken);

        var result = await delivery.SendAsync(notification, attempt.Id, cancellationToken);
        attempt.Complete(result.Outcome, result.ExternalMessageId, result.FailureCode, clock.GetUtcNow());
        // Commit evidence before throwing: Wolverine may retry this job only after proven non-capture.
        await database.SaveChangesAsync(cancellationToken);
        if (result.Outcome == NotificationDeliveryOutcome.NotCaptured)
        {
            throw new NotificationNotCapturedException(result.FailureCode);
        }
    }
}
