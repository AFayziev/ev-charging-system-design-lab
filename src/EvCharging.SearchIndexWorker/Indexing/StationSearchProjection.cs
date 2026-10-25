using Elastic.Clients.Elasticsearch;
using EvCharging.Domain.Equipment;
using EvCharging.SearchIndex;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Indexing;

/// <summary>Reads stations from the primary as search documents, for live updates and for rebuilds alike.</summary>
internal static class StationSearchProjection
{
    // Keyset page of stations in key order, after the given key.
    public static IQueryable<Station> PageAfter(this IQueryable<Station> stations, Guid? after, int size) => stations
        .AsNoTracking()
        .Where(station => after == null || station.Id.CompareTo(after.Value) > 0)
        .OrderBy(station => station.Id)
        .Take(size);

    // Filter, order, and page the stations first; a filter on the projected shape cannot be translated.
    public static IQueryable<StationSearchSource> ToSources(this IQueryable<Station> stations) => stations
        .Select(
            station => new StationSearchSource(
                station.Id,
                station.PublicId,
                station.Name,
                station.Address,
                station.Latitude,
                station.Longitude,
                station.Chargers.Count,
                station.Chargers
                    .SelectMany(charger => charger.Connectors)
                    .OrderBy(connector => connector.Id)
                    .Select(connector => new ConnectorSearchDocument(
                        connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW))
                    .ToList()));

    public static StationSearchDocument ToDocument(StationSearchSource station) => new(
        station.PublicId,
        station.Name,
        station.Address,
        GeoLocation.LatitudeLongitude(new LatLonGeoLocation
        {
            Lat = (double)station.Latitude,
            Lon = (double)station.Longitude
        }),
        station.ChargerCount,
        station.Connectors.Count,
        station.Connectors);
}
