using Elastic.Clients.Elasticsearch;

namespace EvCharging.SearchIndex;

public sealed record StationSearchDocument(
    Guid PublicId,
    string Name,
    string Address,
    GeoLocation Location,
    int ChargerCount,
    int ConnectorCount,
    IReadOnlyList<ConnectorSearchDocument> Connectors,
    IReadOnlyList<string> ChargerNames,
    IReadOnlyList<string> ProviderNames,
    string ConnectorText);
