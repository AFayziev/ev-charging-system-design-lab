namespace EvCharging.ChargingWorker.Providers;

/// <summary>The latest observation a provider has recorded for a session, in canonical units.</summary>
public sealed record ProviderSnapshotResult(
    Guid EventId,
    Guid ExternalSessionId,
    ProviderEquipmentAddress Equipment,
    DateTimeOffset OccurredAt,
    ProviderSessionState State,
    decimal PowerKw,
    decimal EnergyKWh,
    bool IsFinalEnergy);
