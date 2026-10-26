using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using EvCharging.SearchIndex;

namespace EvCharging.SearchApi.Features.Stations;

internal static class StationSearchOptionsQuery
{
    private const string ConnectorsKey = "connectors";
    private const string TypesKey = "types";
    private const string SuppliesKey = "supplies";
    private const string PowersKey = "powers";

    // A bound on distinct values; connector vocabularies are small.
    private const int MaximumValues = 50;

    public static SearchRequest ToSearchRequest() => new(StationSearchIndex.Alias)
    {
        Size = 0,
        TrackTotalHits = new TrackHits(false),
        Aggregations = new Dictionary<string, Aggregation>
        {
            [ConnectorsKey] = new Aggregation
            {
                Nested = new NestedAggregation { Path = StationSearchQuery.Connectors },
                Aggregations = new Dictionary<string, Aggregation>
                {
                    [TypesKey] = Terms(StationSearchQuery.ConnectorType),
                    [SuppliesKey] = Terms(StationSearchQuery.PowerSupply),
                    [PowersKey] = Terms(StationSearchQuery.RatedPowerKW)
                }
            }
        }
    };

    public static StationSearchOptionsResponse ToResponse(SearchResponse<StationSearchDocument> response)
    {
        var connectors = response.Aggregations?.GetNested(ConnectorsKey)?.Aggregations;
        return new StationSearchOptionsResponse(
            Strings(connectors, TypesKey),
            Strings(connectors, SuppliesKey),
            // The index stores power as a float, so a key such as 7.4 reads back as 7.400000095.
            Numbers(connectors, PowersKey).Select(power => Math.Round((decimal)power, 3)).Distinct().Order().ToArray());
    }

    private static TermsAggregation Terms(Field field) => new() { Field = field, Size = MaximumValues };

    private static string[] Strings(AggregateDictionary? aggregations, string key) =>
        aggregations?.GetStringTerms(key)?.Buckets
            .Select(bucket => bucket.Key.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];

    private static double[] Numbers(AggregateDictionary? aggregations, string key) =>
        aggregations?.GetDoubleTerms(key)?.Buckets.Select(bucket => bucket.Key).ToArray() ?? [];
}
