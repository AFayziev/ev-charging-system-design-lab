using Elastic.Clients.Elasticsearch.Mapping;

namespace EvCharging.SearchIndex;

/// <summary>Names and mapping of the station index. Searches use <see cref="Alias" />.</summary>
public static class StationSearchIndex
{
    public const string Alias = "stations";

    /// <summary>Marks the index being built, so live writes reach it before it is swapped in.</summary>
    public const string RebuildAlias = "stations-rebuild";

    public const string IndexPattern = "stations-*";

    /// <summary>Changing the mapping means raising this, which makes the worker rebuild into a new index.</summary>
    public const string MappingVersion = "1";

    public const string MappingVersionKey = "mappingVersion";

    public static Properties<StationSearchDocument> StationProperties() => new()
    {
        { document => document.PublicId, new KeywordProperty() },
        { document => document.Name, TextWithKeyword() },
        { document => document.Address, TextWithKeyword() },
        { document => document.Location, new GeoPointProperty() },
        { document => document.ChargerCount, new IntegerNumberProperty() },
        { document => document.ConnectorCount, new IntegerNumberProperty() },
        { document => document.Connectors, new NestedProperty { Properties = ConnectorProperties() } }
    };

    private static Properties<ConnectorSearchDocument> ConnectorProperties() => new()
    {
        { connector => connector.ConnectorType, new KeywordProperty() },
        { connector => connector.PowerSupply, new KeywordProperty() },
        { connector => connector.RatedPowerKW, new FloatNumberProperty() }
    };

    private static TextProperty TextWithKeyword() => new()
    {
        Fields = new Properties { { "keyword", new KeywordProperty() } }
    };
}
