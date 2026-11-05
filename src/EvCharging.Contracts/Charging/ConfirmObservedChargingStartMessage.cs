namespace EvCharging.Contracts.Charging;

/// <summary>
/// Confirms a session's unsettled start from observations showing it charging or ended; a no-op once it is settled.
/// </summary>
/// <param name="SessionId">The observed session.</param>
/// <param name="ObservedAt">Source time of the earliest such observation.</param>
public sealed record ConfirmObservedChargingStartMessage(Guid SessionId, DateTimeOffset ObservedAt);
