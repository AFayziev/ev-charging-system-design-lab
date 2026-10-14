namespace EvCharging.Persistence.Entities;

public sealed class NotificationDeliveryAttempt
{
    private NotificationDeliveryAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Notification? Notification { get; private set; }
    public int Number { get; private set; }
    public string Provider { get; private set; } = "Mailpit";
    public NotificationDeliveryOutcome Outcome { get; private set; } = NotificationDeliveryOutcome.Uncertain;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public string? FailureCode { get; private set; }

    public static NotificationDeliveryAttempt Start(Notification notification, int number, DateTimeOffset now) => new()
    {
        NotificationId = notification.Id,
        Notification = notification,
        Number = number,
        StartedAt = now
    };

    public void Complete(
        NotificationDeliveryOutcome outcome,
        string? externalMessageId,
        string? failureCode,
        DateTimeOffset now)
    {
        if (Outcome != NotificationDeliveryOutcome.Uncertain || CompletedAt is not null)
        {
            throw new InvalidOperationException("Only an unresolved dispatched attempt can be completed.");
        }

        Outcome = outcome;
        ExternalMessageId = externalMessageId;
        FailureCode = failureCode;
        CompletedAt = now;
    }
}
