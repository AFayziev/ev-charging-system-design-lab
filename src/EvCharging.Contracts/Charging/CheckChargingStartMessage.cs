namespace EvCharging.Contracts.Charging;

public sealed record CheckChargingStartMessage(Guid OperationId, bool Automatic = false);
