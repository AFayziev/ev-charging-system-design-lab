namespace EvCharging.Contracts.Charging;

/// <summary>Requests the numbered provider request for a stop; later numbers are resends.</summary>
public sealed record StopChargingMessage(Guid OperationId, int AttemptNumber = 1);
