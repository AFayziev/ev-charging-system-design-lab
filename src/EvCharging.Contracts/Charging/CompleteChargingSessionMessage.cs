namespace EvCharging.Contracts.Charging;

/// <summary>Completes a session whose provider evidence includes a final reading; a no-op when it cannot complete.</summary>
public sealed record CompleteChargingSessionMessage(Guid SessionId);
