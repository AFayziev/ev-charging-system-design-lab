namespace EvCharging.Contracts.Events.Reservations;

public sealed record ReservationExpired(Guid ReservationId, DateTimeOffset ExpiredAt);
