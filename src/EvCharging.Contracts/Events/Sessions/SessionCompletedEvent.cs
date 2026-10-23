namespace EvCharging.Contracts.Events.Sessions;

public sealed record SessionCompletedEvent(
    Guid SessionId,
    Guid ReservationId,
    decimal FinalEnergyKWh,
    DateTimeOffset CompletedAt);
