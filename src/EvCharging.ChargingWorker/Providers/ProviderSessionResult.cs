namespace EvCharging.ChargingWorker.Providers;

internal enum ProviderSessionState
{
    Charging
}

internal sealed record ProviderSessionResult(
    Guid Id,
    Guid Reference,
    ProviderEquipmentAddress Equipment,
    DateTimeOffset StartedAt,
    decimal PowerKw,
    ProviderSessionState State);
