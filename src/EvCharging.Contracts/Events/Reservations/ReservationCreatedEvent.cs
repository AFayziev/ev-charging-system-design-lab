namespace EvCharging.Contracts.Events.Reservations;

public sealed record ReservationCreatedEvent(Guid ReservationId, DateTimeOffset EndsAt);
