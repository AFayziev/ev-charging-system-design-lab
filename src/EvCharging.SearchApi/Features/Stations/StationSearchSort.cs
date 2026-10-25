namespace EvCharging.SearchApi.Features.Stations;

/// <summary>The order of a search: nearest first when a location is given, best match for text, otherwise by name.</summary>
internal enum StationSearchSort
{
    Distance,
    Relevance,
    Name
}
