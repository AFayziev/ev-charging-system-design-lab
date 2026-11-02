namespace EvCharging.SearchApi.Features.Stations;

/// <summary>Mirrors a station in the browse list, plus the distance when the search is by location.</summary>
public sealed record StationSearchResultResponse(
    Guid Id,
    string Name,
    string Address,
    int ChargerCount,
    int ConnectorCount,
    IReadOnlyList<ConnectorCapabilityResponse> Capabilities,
    StationPhotoResponse? CoverPhoto,
    double? DistanceKm);

/// <summary>A published photo, with the variants a card and a full view load.</summary>
public sealed record StationPhotoResponse(Guid Id, Uri ThumbnailUrl, Uri DisplayUrl);
