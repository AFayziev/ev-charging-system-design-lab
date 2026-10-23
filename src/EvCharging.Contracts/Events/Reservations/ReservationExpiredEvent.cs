namespace EvCharging.Contracts.Events.Reservations;

public sealed record ReservationExpiredEvent(Guid ReservationId, DateTimeOffset ExpiredAt);
