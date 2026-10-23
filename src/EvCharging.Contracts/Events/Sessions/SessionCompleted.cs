namespace EvCharging.Contracts.Events.Sessions;

public sealed record SessionCompleted(
    Guid SessionId,
    Guid ReservationId,
    decimal FinalEnergyKWh,
    DateTimeOffset CompletedAt);
