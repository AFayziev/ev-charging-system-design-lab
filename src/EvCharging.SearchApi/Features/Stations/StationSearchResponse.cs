namespace EvCharging.SearchApi.Features.Stations;

/// <summary>
/// One page of results. <see cref="Total" /> counts every match of the search, not the page; when
/// <see cref="TotalIsExact" /> is false there are at least that many.
/// </summary>
public sealed record StationSearchResponse(
    IReadOnlyList<StationSearchResultResponse> Stations,
    string? NextCursor,
    long Total,
    bool TotalIsExact);
