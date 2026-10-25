using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;

namespace EvCharging.SearchIndexWorker.Indexing;

public sealed class StationSearchIndexWriter(ElasticsearchClient client)
{
    /// <summary>
    /// Writes the station to the serving index and, while one is being built, to the index being built.
    /// Returns false when neither exists, so the caller can request a build.
    /// </summary>
    public async Task<bool> IndexAsync(StationSearchDocument document, CancellationToken cancellationToken)
    {
        // require_alias stops a missing alias from becoming a dynamically mapped index of that name.
        var id = document.PublicId.ToString();
        var response = await client.BulkAsync(
            new BulkRequest
            {
                Operations = new[] { StationSearchIndex.Alias, StationSearchIndex.RebuildAlias }
                    .Select(IBulkOperation (alias) => new BulkIndexOperation<StationSearchDocument>(document)
                    {
                        Index = alias,
                        Id = id,
                        RequireAlias = true
                    })
                    .ToList()
            },
            cancellationToken);
        SearchIndexResponses.ThrowIfBulkRequestFailed(response, "index station");

        // A 404 only means that alias does not exist right now, which is normal for the rebuild alias.
        var failed = response.Items.FirstOrDefault(
            item => item.Status != 404 && !SearchIndexResponses.IsWritten(item.Status));
        if (failed is not null)
        {
            SearchIndexResponses.Throw(failed.Status, $"Search index failed to index station: {failed.Error?.Reason}");
        }

        return response.Items.Any(item => SearchIndexResponses.IsWritten(item.Status));
    }
}
