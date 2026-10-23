namespace EvCharging.Contracts.Charging;

/// <summary>Requests the numbered provider request for a start; later numbers are scheduled retries.</summary>
public sealed record StartChargingMessage(Guid OperationId, int AttemptNumber = 1);
