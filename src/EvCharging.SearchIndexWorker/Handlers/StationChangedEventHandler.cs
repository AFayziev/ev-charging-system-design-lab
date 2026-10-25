using Elastic.Clients.Elasticsearch;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Persistence;
using EvCharging.SearchIndexWorker.Indexing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Handlers;

public static class StationChangedEventHandler
{
    // Events for one station run one at a time, and each reads the committed row, so the last write is the newest.
    public static async Task HandleAsync(
        StationChangedEvent message,
        PlatformDbContext database,
        StationSearchIndexWriter writer,
        CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .Where(station => station.Id == message.StationId)
            .Select(
                station => new
                {
                    station.PublicId,
                    station.Name,
                    station.Address,
                    station.Latitude,
                    station.Longitude,
                    ChargerCount = station.Chargers.Count,
                    Connectors = station.Chargers
                        .SelectMany(charger => charger.Connectors)
                        .OrderBy(connector => connector.Id)
                        .Select(connector => new ConnectorSearchDocument(
                            connector.ConnectorType, connector.PowerSupply, connector.RatedPowerKW))
                        .ToList()
                })
            .SingleAsync(cancellationToken);
        var document = new StationSearchDocument(
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

        await writer.IndexAsync(document, cancellationToken);
    }
}
