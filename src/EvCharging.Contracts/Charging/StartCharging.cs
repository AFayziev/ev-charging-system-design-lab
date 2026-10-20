namespace EvCharging.Contracts.Charging;

/// <summary>Requests the numbered provider request for a start; later numbers are scheduled retries.</summary>
public sealed record StartCharging(Guid OperationId, int AttemptNumber = 1);
