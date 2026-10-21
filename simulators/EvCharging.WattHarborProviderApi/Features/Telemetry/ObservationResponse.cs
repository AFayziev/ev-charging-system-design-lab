using EvCharging.WattHarborProviderApi.Persistence;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

/// <summary>One observation. The callback body and the session snapshot carry the same shape and identity.</summary>
public sealed record ObservationResponse(
    Guid EventId,
    Guid ExternalSessionId,
    string StationId,
    string ChargerId,
    string ConnectorId,
    DateTimeOffset OccurredAt,
    ObservationState State,
    decimal PowerKw,
    decimal EnergyWh,
    bool IsFinalEnergy);

internal static class ObservationMappingExtensions
{
    public static ObservationResponse ToResponse(this ProviderObservation observation)
    {
        var session = observation.Session
            ?? throw new InvalidOperationException("An observation response requires its session.");
        return new ObservationResponse(
            observation.EventId,
            session.ExternalSessionId,
            session.StationId,
            session.ChargerId,
            session.ConnectorId,
            observation.OccurredAt,
            observation.State,
            observation.PowerKw,
            observation.EnergyWh,
            observation.IsFinalEnergy);
    }
}
