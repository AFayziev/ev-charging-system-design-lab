using Elastic.Clients.Elasticsearch;

namespace EvCharging.SearchIndexWorker.Indexing;

public sealed record StationSearchDocument(
    Guid PublicId,
    string Name,
    string Address,
    GeoLocation Location,
    int ChargerCount,
    int ConnectorCount,
    IReadOnlyList<ConnectorSearchDocument> Connectors);

public sealed record ConnectorSearchDocument(string ConnectorType, string PowerSupply, decimal RatedPowerKW);
