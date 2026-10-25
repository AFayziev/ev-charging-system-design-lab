namespace EvCharging.SearchApi.Features.Stations;

public sealed record StationSearchResponse(IReadOnlyList<StationSearchResultResponse> Stations, string? NextCursor);
