using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexManagement;
using EvCharging.Persistence;
using EvCharging.SearchIndex;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Indexing;

/// <summary>
/// Builds the station index from the primary into a new index behind the <c>stations-rebuild</c> alias, then swaps
/// the <c>stations</c> alias onto it. Live writes reach the new index while it loads, so edits made during the
/// load are not lost, and the bulk load never overwrites them.
/// </summary>
public sealed class StationSearchIndexBuilder(ElasticsearchClient client)
{
    private const int PageSize = 500;

    public async Task EnsureAsync(PlatformDbContext database, Guid buildId, CancellationToken cancellationToken)
    {
        var indices = await GetIndicesAsync(cancellationToken);
        var serving = indices.Where(index => index.Serving).ToList();
        // Leftovers of an interrupted build go first, so no alias ever spans two indices.
        await DeleteAsync(indices.Except(serving), cancellationToken);
        if (!NeedsBuild(serving))
        {
            return;
        }

        var created = $"stations-{buildId:N}";
        await CreateAsync(created, cancellationToken);
        await LoadAsync(database, created, cancellationToken);
        await SwapAsync(created, serving, cancellationToken);
        await DeleteAsync(serving, cancellationToken);
    }

    private async Task<List<IndexInfo>> GetIndicesAsync(CancellationToken cancellationToken)
    {
        var response = await client.Indices.GetAsync(StationSearchIndex.IndexPattern, cancellationToken);
        SearchIndexResponses.ThrowIfFailed(response, "list indices", allowNotFound: true);

        return response.Indices.Select(
            index => new IndexInfo(
                index.Key.ToString(),
                index.Value.Aliases?.ContainsKey(StationSearchIndex.Alias) == true,
                MappingVersionOf(index.Value)))
            .ToList();
    }

    // Stations reach the index through events, so only a missing or outdated index needs a build.
    private static bool NeedsBuild(List<IndexInfo> serving) =>
        serving is not [{ MappingVersion: StationSearchIndex.MappingVersion }];

    private async Task CreateAsync(string index, CancellationToken cancellationToken)
    {
        var response = await client.Indices.CreateAsync(
            index,
            request => request
                .Aliases(aliases => aliases.Add(StationSearchIndex.RebuildAlias, _ => { }))
                .Mappings(mappings => mappings
                    .Properties(StationSearchIndex.StationProperties())
                    .Meta(new Dictionary<string, object>
                    {
                        [StationSearchIndex.MappingVersionKey] = StationSearchIndex.MappingVersion
                    })),
            cancellationToken);
        SearchIndexResponses.ThrowIfFailed(response, "create index");
    }

    private async Task LoadAsync(PlatformDbContext database, string index, CancellationToken cancellationToken)
    {
        Guid? after = null;
        while (true)
        {
            var page = await database.Stations
                .PageAfter(after, PageSize)
                .ToSources()
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            // Create never overwrites, so a live write that already reached this index stays.
            var response = await client.BulkAsync(
                new BulkRequest
                {
                    Operations = page
                        .Select(StationSearchProjection.ToDocument)
                        .Select(IBulkOperation (document) => new BulkCreateOperation<StationSearchDocument>(document)
                        {
                            Index = index,
                            Id = document.PublicId.ToString()
                        })
                        .ToList()
                },
                cancellationToken);
            SearchIndexResponses.ThrowIfBulkRequestFailed(response, "load stations");
            foreach (var item in response.Items
                .Where(item => item.Status != 409 && !SearchIndexResponses.IsWritten(item.Status)))
            {
                SearchIndexResponses.Throw(item.Status, $"Search index failed to load a station: {item.Error?.Reason}");
            }

            after = page[^1].Id;
        }

        var refreshed = await client.Indices.RefreshAsync(index, cancellationToken);
        SearchIndexResponses.ThrowIfFailed(refreshed, "refresh index");
    }

    // One request, so searches see the old index or the new one and nothing in between.
    private async Task SwapAsync(string created, IEnumerable<IndexInfo> previous, CancellationToken cancellationToken)
    {
        var actions = previous
            .Select(index => (Action<IndexUpdateAliasesActionDescriptor>)(action => action
                .Remove(remove => remove.Index(index.Name).Alias(StationSearchIndex.Alias))))
            .Append(action => action.Add(add => add.Index(created).Alias(StationSearchIndex.Alias)))
            .Append(action => action.Remove(remove => remove.Index(created).Alias(StationSearchIndex.RebuildAlias)))
            .ToArray();
        var response = await client.Indices.UpdateAliasesAsync(request => request.Actions(actions), cancellationToken);
        SearchIndexResponses.ThrowIfFailed(response, "swap alias");
    }

    private async Task DeleteAsync(IEnumerable<IndexInfo> indices, CancellationToken cancellationToken)
    {
        foreach (var index in indices.ToList())
        {
            var response = await client.Indices.DeleteAsync(index.Name, cancellationToken);
            SearchIndexResponses.ThrowIfFailed(response, "delete index", allowNotFound: true);
        }
    }

    private static string? MappingVersionOf(IndexState index) =>
        index.Mappings?.Meta is { } meta && meta.TryGetValue(StationSearchIndex.MappingVersionKey, out var version)
            ? version.ToString()
            : null;

    private sealed record IndexInfo(string Name, bool Serving, string? MappingVersion);
}
