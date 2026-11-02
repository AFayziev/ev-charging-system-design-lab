using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using EvCharging.S3ObjectStore;

namespace EvCharging.Api.Features.Stations;

internal static class StationMappings
{
    public static StationSummaryResponse ToResponse(
        this Station station,
        int chargerCount,
        IReadOnlyList<Connector> connectors,
        StationPhotoResponse? coverPhoto)
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
            connectors.Count,
            capabilities,
            coverPhoto);
    }

    public static StationDetailResponse ToResponse(
        this Station station, bool canEdit, IReadOnlyList<StationMediaResponse> media) => new(
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
            media,
            canEdit);

    public static ChargerResponse ToResponse(this Charger charger) => new(
        charger.PublicId,
        charger.Name,
        charger.ProviderCharger?.ProviderStation?.ChargingProvider?.Name,
        charger.Connectors
            .OrderBy(connector => connector.Number)
            .Select(connector => connector.ToResponse())
            .ToArray());

    public static ConnectorResponse ToResponse(this Connector connector) => new(
        connector.PublicId, connector.Number, connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW);

    public static ConnectorCapabilityResponse ToCapabilityResponse(this Connector connector) => new(
        connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW);

    public static StationMediaResponse ToMediaResponse(
        this StationMediaUrls urls, Guid stationId, Guid itemId, StationMediaKind kind)
    {
        if (kind == StationMediaKind.Video)
        {
            return new(itemId, kind, null, null, urls.Video(stationId, itemId));
        }

        var photo = urls.ToPhotoResponse(stationId, itemId);
        return new(itemId, kind, photo.ThumbnailUrl, photo.DisplayUrl, null);
    }

    public static StationPhotoResponse ToPhotoResponse(this StationMediaUrls urls, Guid stationId, Guid photoId) =>
        new(photoId, urls.Thumbnail(stationId, photoId), urls.Display(stationId, photoId));
}
