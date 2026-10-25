using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Transport.Products.Elasticsearch;

namespace EvCharging.SearchIndexWorker.Indexing;

public sealed class StationSearchIndexWriter(ElasticsearchClient client)
{
    public const string Alias = "stations";
    public const string InitialIndex = "stations-v1";

    private readonly SemaphoreSlim _creation = new(1, 1);
    private volatile bool _ready;

    public async Task IndexAsync(StationSearchDocument document, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken);
        // Writing through a missing alias would make Elasticsearch create a dynamically mapped index of that name.
        var response = await client.IndexAsync(
            document, request => request.Index(Alias).Id(document.PublicId).RequireAlias(true), cancellationToken);
        if (response.ApiCallDetails.HttpStatusCode == 404)
        {
            // The index was removed after it was created, so the next attempt creates it again.
            _ready = false;
        }

        ThrowIfFailed(response, "index station");
    }

    // Created on first write, not at startup, so an unavailable Elasticsearch only delays events.
    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        await _creation.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
            {
                return;
            }

            var exists = await client.Indices.ExistsAsync(Alias, cancellationToken);
            ThrowIfFailed(exists, "check index", allowNotFound: true);
            if (!exists.Exists)
            {
                var created = await client.Indices.CreateAsync(
                    InitialIndex,
                    request => request
                        .Aliases(aliases => aliases.Add(Alias, _ => { }))
                        .Mappings(mappings => mappings.Properties(StationProperties())),
                    cancellationToken);
                if (!created.IsValidResponse && created.ElasticsearchServerError?.Error?.Type
                    != "resource_already_exists_exception")
                {
                    ThrowIfFailed(created, "create index");
                }
            }

            _ready = true;
        }
        finally
        {
            _creation.Release();
        }
    }

    private static Properties<StationSearchDocument> StationProperties() => new()
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

    private static void ThrowIfFailed(ElasticsearchResponse response, string action, bool allowNotFound = false)
    {
        if (response.IsValidResponse || (allowNotFound && response.ApiCallDetails.HttpStatusCode == 404))
        {
            return;
        }

        int? status = response.ApiCallDetails.HttpStatusCode;
        string message = $"Search index failed to {action}: {response.ApiCallDetails.DebugInformation}";
        // Only a malformed or oversized document can never succeed; blocks, throttling, and auth failures can clear.
        if (status is 400 or 413)
        {
            throw new InvalidOperationException(message, response.ApiCallDetails.OriginalException);
        }

        throw new SearchIndexUnavailableException(message, response.ApiCallDetails.OriginalException);
    }
}
