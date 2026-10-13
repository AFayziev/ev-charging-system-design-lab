namespace EvCharging.Contracts.Events.Reservations;

public sealed record ReservationCreated(Guid ReservationId, DateTimeOffset EndsAt);
