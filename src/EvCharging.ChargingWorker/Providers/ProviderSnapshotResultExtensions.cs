using EvCharging.Contracts.Observations;

namespace EvCharging.ChargingWorker.Providers;

internal static class ProviderSnapshotResultExtensions
{
    /// <summary>The stream form of a polled snapshot. The provider's event identity and occurrence time are kept.</summary>
    public static ObservationEnvelope ToEnvelope(
        this ProviderSnapshotResult snapshot,
        string integrationKey,
        Guid observationId,
        DateTimeOffset receivedAt) =>
        new(
            ObservationEnvelope.CurrentSchemaVersion,
            observationId,
            integrationKey,
            snapshot.EventId,
            snapshot.ExternalSessionId,
            snapshot.Equipment.StationId,
            snapshot.Equipment.ChargerId,
            snapshot.Equipment.ConnectorId,
            snapshot.OccurredAt,
            receivedAt.ToUniversalTime(),
            snapshot.State switch
            {
                ProviderSessionState.Charging => ObservationState.Charging,
                ProviderSessionState.Stopped => ObservationState.Stopped,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(snapshot), snapshot.State, "Unknown provider session state.")
            },
            snapshot.PowerKw,
            snapshot.EnergyKWh,
            snapshot.IsFinalEnergy);
}
