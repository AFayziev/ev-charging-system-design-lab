using System.Globalization;
using System.Linq.Expressions;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using EvCharging.SearchIndex;

namespace EvCharging.SearchApi.Features.Stations;

internal static class StationSearchQuery
{
    private static readonly Expression<Func<StationSearchDocument, object>> NameExpression = document => document.Name;
    private static readonly Field Name = Infer.Field<StationSearchDocument>(document => document.Name, boost: 3);
    private static readonly Field NameKeyword = Infer.Field<StationSearchDocument, object>(
        NameExpression.AppendSuffix("keyword"));
    private static readonly Field Address = Infer.Field<StationSearchDocument>(document => document.Address);
    private static readonly Field ChargerNames = Infer.Field<StationSearchDocument>(document => document.ChargerNames);
    private static readonly Field ProviderNames = Infer.Field<StationSearchDocument>(
        document => document.ProviderNames);
    private static readonly Field ConnectorText = Infer.Field<StationSearchDocument>(
        document => document.ConnectorText);
    private static readonly Field[] SearchedText =
        [Name, Address, ChargerNames, ProviderNames, ConnectorText];
    private static readonly Field Location = Infer.Field<StationSearchDocument>(document => document.Location);
    private static readonly Fields NotReturned = new Field[]
    {
        Location, ChargerNames, ProviderNames, ConnectorText
    };
    private static readonly Field PublicId = Infer.Field<StationSearchDocument>(document => document.PublicId);
    internal static readonly Field Connectors = Infer.Field<StationSearchDocument>(document => document.Connectors);
    internal static readonly Field ConnectorType = Infer.Field<StationSearchDocument>(
        document => document.Connectors[0].ConnectorType);
    internal static readonly Field PowerSupply = Infer.Field<StationSearchDocument>(
        document => document.Connectors[0].PowerSupply);
    internal static readonly Field RatedPowerKW = Infer.Field<StationSearchDocument>(
        document => document.Connectors[0].RatedPowerKW);

    /// <summary>Asks for one more station than the page holds, to tell whether another page follows.</summary>
    public static SearchRequest ToSearchRequest(this SearchStationsRequest request, FieldValue[] after) =>
        new(StationSearchIndex.Alias)
        {
            Query = ToQuery(request),
            Size = request.Limit + 1,
            Sort = ToSort(request),
            SearchAfter = after.Length > 0 ? after : null,
            TrackTotalHits = new TrackHits(StationSearchLimits.TrackedTotal),
            Source = new SourceConfig(new SourceFilter { Excludes = NotReturned })
        };

    private static Query ToQuery(SearchStationsRequest request)
    {
        var must = new List<Query>();
        var filter = new List<Query>();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            // Whole words tolerate typos; the last word may still be half typed ("watt" for "WattHarbor").
            must.Add(new BoolQuery
            {
                Should =
                [
                    new MultiMatchQuery
                    {
                        Query = request.Q,
                        Fields = SearchedText,
                        Fuzziness = new Fuzziness("AUTO")
                    },
                    new MultiMatchQuery
                    {
                        Query = request.Q,
                        Fields = SearchedText,
                        Type = TextQueryType.BoolPrefix
                    }
                ],
                MinimumShouldMatch = 1
            });
        }

        if (ToConnectorFilters(request) is { Count: > 0 } connectorFilters)
        {
            // One nested query keeps type, supply and power on the same connector.
            filter.Add(new NestedQuery
            {
                Path = Connectors,
                Query = new BoolQuery { Filter = connectorFilters }
            });
        }

        if (request is { Lat: { } lat, Lon: { } lon, RadiusKm: { } radius })
        {
            filter.Add(new GeoDistanceQuery
            {
                Field = Location,
                Distance = $"{radius.ToString("0.###", CultureInfo.InvariantCulture)}km",
                Location = GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = lat, Lon = lon })
            });
        }

        return new BoolQuery { Must = must.Count > 0 ? must : null, Filter = filter.Count > 0 ? filter : null };
    }

    private static List<Query> ToConnectorFilters(SearchStationsRequest request)
    {
        var filters = new List<Query>();
        if (request.ConnectorType is { Length: > 0 } types)
        {
            filters.Add(Terms(ConnectorType, types));
        }

        if (request.PowerSupply is { Length: > 0 } supplies)
        {
            filters.Add(Terms(PowerSupply, supplies));
        }

        if (request.MinPowerKw is { } minimum)
        {
            filters.Add(new NumberRangeQuery
            {
                Field = RatedPowerKW,
                Gte = (double)minimum
            });
        }

        return filters;
    }

    private static TermsQuery Terms(Field field, string[] values) => new()
    {
        Field = field,
        Terms = new TermsQueryField(values.Select(FieldValue.String).ToArray())
    };

    private static SortOptions[] ToSort(SearchStationsRequest request)
    {
        var leading = request.Sort switch
        {
            StationSearchSort.Distance => new SortOptions { GeoDistance = ToDistanceSort(request) },
            StationSearchSort.Relevance => new SortOptions { Score = new ScoreSort { Order = SortOrder.Desc } },
            _ => new SortOptions { Field = new FieldSort { Field = NameKeyword, Order = SortOrder.Asc } }
        };
        var publicId = new SortOptions { Field = new FieldSort { Field = PublicId, Order = SortOrder.Asc } };
        return [leading, publicId];
    }

    private static GeoDistanceSort ToDistanceSort(SearchStationsRequest request) => new()
    {
        Field = Location,
        Location = [GeoLocation.LatitudeLongitude(
            new LatLonGeoLocation { Lat = request.Lat!.Value, Lon = request.Lon!.Value })],
        Order = SortOrder.Asc,
        Unit = DistanceUnit.Kilometers
    };
}
