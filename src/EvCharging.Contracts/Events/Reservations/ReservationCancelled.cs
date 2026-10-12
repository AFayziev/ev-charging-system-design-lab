namespace EvCharging.Contracts.Events.Reservations;

public sealed record ReservationCancelled(Guid ReservationId)
{
    public const string Queue = "reservation-cancelled";
}
