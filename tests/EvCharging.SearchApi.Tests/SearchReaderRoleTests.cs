using System.Net;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport.Products.Elasticsearch;
using EvCharging.SearchIndex;
using Xunit;
using static EvCharging.SearchApi.Tests.StationDocuments;

namespace EvCharging.SearchApi.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class SearchReaderRoleTests(SecuredSearchIndexFixture index) : IAsyncLifetime
{
    private readonly StationSearchDocument _station = Station(
        "Role test", "Anywhere", 51.5, -0.1, Connector("Type2", "AC", 22));

    public async Task InitializeAsync() => await index.ResetIndexAsync(_station);

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(ForbiddenCalls))]
    public async Task Reader_IsRefused(string description, Func<ElasticsearchClient, Task<ElasticsearchResponse>> call)
    {
        var response = await call(index.ReaderClient);

        Assert.True(HttpStatusCode.Forbidden == (HttpStatusCode)response.ApiCallDetails.HttpStatusCode!, description);
    }

    public static TheoryData<string, Func<ElasticsearchClient, Task<ElasticsearchResponse>>> ForbiddenCalls() => new()
    {
        {
            "write to the serving alias",
            async client => await client.IndexAsync(
                Station("Denied", "Anywhere", 51.5, -0.1), request => request.Index(StationSearchIndex.Alias))
        },
        {
            "read the rebuild alias",
            async client =>
                await client.SearchAsync<StationSearchDocument>(StationSearchIndex.RebuildAlias)
        },
        {
            "read other indices",
            async client =>
                await client.SearchAsync<StationSearchDocument>("other-index")
        }
    };
}
