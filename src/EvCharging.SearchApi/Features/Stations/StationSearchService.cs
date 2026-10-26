using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Transport.Products.Elasticsearch;
using EvCharging.SearchIndex;
using Polly;
using Polly.Registry;

namespace EvCharging.SearchApi.Features.Stations;

internal sealed class StationSearchService(
    ElasticsearchClient client,
    ResiliencePipelineProvider<string> pipelines,
    ILogger<StationSearchService> logger)
{
    private readonly ResiliencePipeline _pipeline = pipelines.GetPipeline(StationSearchResilience.PipelineKey);

    public async Task<StationSearchResponse> SearchAsync(StationSearchPage page, CancellationToken cancellationToken)
    {
        var request = page.Request;
        var search = request.ToSearchRequest(page.After);
        var response = await ExecuteAsync(search, cancellationToken);

        var hits = response.Hits;
        var kept = hits.Take(request.Limit).ToArray();
        var nextCursor = hits.Count > kept.Length ? StationSearchCursor.Encode(request, kept[^1].Sort!) : null;
        var (total, exact) = Total(response);
        return new StationSearchResponse(
            kept.Select(hit => ToResult(hit, request.Sort)).ToArray(),
            nextCursor,
            total,
            exact);
    }

    public async Task<StationSearchOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var search = StationSearchOptionsQuery.ToSearchRequest();
        return StationSearchOptionsQuery.ToResponse(await ExecuteAsync(search, cancellationToken));
    }

    private async Task<SearchResponse<StationSearchDocument>> ExecuteAsync(
        SearchRequest search,
        CancellationToken cancellationToken) =>
        await _pipeline.ExecuteAsync(
            async token =>
            {
                var attempt = await client.SearchAsync<StationSearchDocument>(search, token);
                if (!attempt.IsValidResponse)
                {
                    ThrowFailed(attempt, token);
                }

                return attempt;
            },
            cancellationToken);

    private static (long Total, bool Exact) Total(SearchResponse<StationSearchDocument> response) =>
        response.HitsMetadata.Total switch
        {
            { Value1: { } counted } => (counted.Value, counted.Relation == TotalHitsRelation.Eq),
            { Value2: { } exact } => (exact, true),
            _ => (response.Hits.Count, true)
        };

    private static StationSearchResultResponse ToResult(Hit<StationSearchDocument> hit, StationSearchSort sort)
    {
        var station = hit.Source!;
        var capabilities = station.Connectors
            .Select(connector => new ConnectorCapabilityResponse(
                connector.ConnectorType,
                connector.PowerSupply,
                connector.RatedPowerKW))
            .Distinct()
            .OrderBy(capability => capability.ConnectorType)
            .ThenBy(capability => capability.PowerSupply)
            .ThenBy(capability => capability.RatedPowerKW)
            .ToArray();
        return new StationSearchResultResponse(
            station.PublicId,
            station.Name,
            station.Address,
            station.ChargerCount,
            station.ConnectorCount,
            capabilities,
            sort == StationSearchSort.Distance ? DistanceKm(hit) : null);
    }

    private static double? DistanceKm(Hit<StationSearchDocument> hit) =>
        hit.Sort?.FirstOrDefault() is { } value && value.TryGetDouble(out var distance) ? distance : null;

    private void ThrowFailed(ElasticsearchResponse response, CancellationToken cancellationToken)
    {
        // An abandoned request is not an index outage.
        cancellationToken.ThrowIfCancellationRequested();

        var details = response.ApiCallDetails;
        // Only a rejected query is a defect of this service; a missing index, throttling, outages and timeouts clear.
        if (details.HttpStatusCode == 400)
        {
            throw new InvalidOperationException(
                $"Search query was rejected: {details.DebugInformation}",
                details.OriginalException);
        }

        logger.LogWarning(
            details.OriginalException,
            "Search index is unavailable (status {Status}).",
            details.HttpStatusCode);
        throw new SearchUnavailableException("The search index is unavailable.", details.OriginalException);
    }
}
