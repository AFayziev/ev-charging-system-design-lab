namespace EvCharging.Contracts.Charging;

public sealed record CheckChargingStart(Guid OperationId, bool Automatic = false);

public sealed record CheckChargingStop(Guid OperationId);

public sealed record ChargingCheckResult(ChargingCheckOutcome Outcome);

public enum ChargingCheckOutcome
{
    Resolved,
    Unresolved,
    Unavailable
}
