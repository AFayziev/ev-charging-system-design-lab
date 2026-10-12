using EvCharging.Domain.Equipment;

namespace EvCharging.Api.Features.Stations;

internal static class StationResponseMappings
{
    public static StationSummaryResponse ToResponse(
        this Station station,
        int chargerCount,
        int evseCount,
        IReadOnlyList<Connector> connectors)
    {
        var capabilities = connectors.Select(connector => connector.ToCapabilityResponse())
            .Distinct()
            .OrderBy(capability => capability.ConnectorType)
            .ThenBy(capability => capability.PowerSupply)
            .ThenBy(capability => capability.RatedPowerKW)
            .ToArray();
        return new(
            station.PublicId,
            station.Name,
            station.Address,
            chargerCount,
            evseCount,
            connectors.Count,
            capabilities);
    }

    public static StationDetailResponse ToResponse(this Station station, bool canEdit) => new(
        station.PublicId,
        station.Name,
        station.Address,
        station.Latitude,
        station.Longitude,
        station.TimeZoneId,
        station.AccessInstructions,
        station.Chargers
            .OrderBy(charger => charger.Id)
            .Select(charger => charger.ToResponse())
            .ToArray(),
        canEdit);

    public static ChargerResponse ToResponse(this Charger charger) => new(
        charger.PublicId,
        charger.Name,
        charger.Evses
            .OrderBy(evse => evse.Number)
            .Select(evse => evse.ToResponse())
            .ToArray());

    public static EvseResponse ToResponse(this Evse evse) => new(
        evse.PublicId,
        evse.Number,
        evse.Connectors
            .OrderBy(connector => connector.Number)
            .Select(connector => connector.ToResponse())
            .ToArray());

    public static ConnectorResponse ToResponse(this Connector connector) => new(
        connector.PublicId, connector.Number, connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW);

    public static ConnectorCapabilityResponse ToCapabilityResponse(this Connector connector) => new(
        connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW);
}
