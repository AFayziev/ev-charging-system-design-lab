using EvCharging.SearchIndex;

namespace EvCharging.SearchIndexWorker.Indexing;

internal sealed record StationSearchSource(
    Guid Id,
    Guid PublicId,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    int ChargerCount,
    List<ConnectorSearchDocument> Connectors);
