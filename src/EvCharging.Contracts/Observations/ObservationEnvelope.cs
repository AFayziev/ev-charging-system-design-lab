namespace EvCharging.Contracts.Observations;

/// <summary>
/// A provider observation accepted by ingestion, in canonical units. Consumers deduplicate by
/// <see cref="IntegrationKey"/> and <see cref="SourceEventId"/> and order by <see cref="OccurredAt"/>.
/// </summary>
/// <param name="SchemaVersion">The envelope schema version.</param>
/// <param name="ObservationId">The platform identity of this accepted envelope.</param>
/// <param name="IntegrationKey">The provider integration that supplied the observation.</param>
/// <param name="SourceEventId">The provider event identity, unique within the integration.</param>
/// <param name="ExternalSessionId">The session identity sent to the provider when the session was started.</param>
/// <param name="StationId">The provider station address component.</param>
/// <param name="ChargerId">The provider charger address component.</param>
/// <param name="ConnectorId">The provider connector address component.</param>
/// <param name="OccurredAt">The provider-reported instant, in UTC.</param>
/// <param name="ReceivedAt">The platform receipt instant, in UTC.</param>
/// <param name="State">The observed physical state.</param>
/// <param name="PowerKw">The instantaneous power in kilowatts.</param>
/// <param name="EnergyKWh">The cumulative session energy in kilowatt-hours.</param>
/// <param name="IsFinalEnergy">Whether the energy is the provider's explicitly final reading.</param>
public sealed record ObservationEnvelope(
    int SchemaVersion,
    Guid ObservationId,
    string IntegrationKey,
    Guid SourceEventId,
    Guid ExternalSessionId,
    string StationId,
    string ChargerId,
    string ConnectorId,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    ObservationState State,
    decimal PowerKw,
    decimal EnergyKWh,
    bool IsFinalEnergy)
{
    public const int CurrentSchemaVersion = 1;
}
