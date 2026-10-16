namespace EvCharging.Contracts.Charging;

public sealed record CheckChargingStart(Guid OperationId);

public sealed record ChargingStartCheckResult(ChargingStartCheckOutcome Outcome);

public enum ChargingStartCheckOutcome
{
    Resolved,
    Unresolved,
    Unavailable
}
