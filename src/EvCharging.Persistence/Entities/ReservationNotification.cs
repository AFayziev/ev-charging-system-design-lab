namespace EvCharging.Persistence.Entities;

public sealed class ReservationNotification
{
    public Guid Id { get; init; }
    public Guid ReservationId { get; init; }
    public ReservationNotificationKind Kind { get; init; }
    public Guid NotificationId { get; init; }
}
