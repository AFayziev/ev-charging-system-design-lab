using Elastic.Clients.Elasticsearch;
using EvCharging.SearchIndex;

namespace EvCharging.SearchApi.Tests;

internal static class StationDocuments
{
    public static StationSearchDocument Station(
        string name,
        string address,
        double latitude,
        double longitude,
        params ConnectorSearchDocument[] connectors) => new(
            Guid.NewGuid(),
            name,
            address,
            GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = latitude, Lon = longitude }),
            ChargerCount: 1,
            connectors.Length,
            connectors);

    public static ConnectorSearchDocument Connector(string type, string supply, decimal kilowatts) =>
        new(type, supply, kilowatts);
}
