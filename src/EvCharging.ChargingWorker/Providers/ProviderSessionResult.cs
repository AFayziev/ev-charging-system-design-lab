namespace EvCharging.ChargingWorker.Providers;

public enum ProviderSessionState
{
    Charging
}

public sealed record ProviderSessionResult(
    Guid Id,
    Guid ExternalSessionId,
    ProviderEquipmentAddress Equipment,
    DateTimeOffset StartedAt,
    decimal PowerKw,
    ProviderSessionState State);
