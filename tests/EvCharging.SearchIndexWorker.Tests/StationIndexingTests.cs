using Elastic.Clients.Elasticsearch;
using EvCharging.SearchIndex;
using EvCharging.SearchIndexWorker.Indexing;
using EvCharging.Testing;
using Xunit;

namespace EvCharging.SearchIndexWorker.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class StationIndexingTests(ElasticsearchFixture search, PlatformDatabaseFixture database)
{
    [Fact]
    public async Task Handle_Event_IndexesProjectedDocument()
    {
        var station = await database.SeedStationAsync();

        await search.IndexStationAsync(database, station.Id);

        var document = await search.GetStationAsync(station.PublicId);
        Assert.NotNull(document);
        Assert.Equal(station.Name, document.Name);
        Assert.Equal(station.Address, document.Address);
        Assert.Equal(1, document.ChargerCount);
        Assert.Equal(2, document.ConnectorCount);
        Assert.Equivalent(
            new[]
            {
                new ConnectorSearchDocument("Type2", "AC", 22m),
                new ConnectorSearchDocument("CCS2", "DC", 50m)
            },
            document.Connectors);
        Assert.Equal(["Charger"], document.ChargerNames);
        Assert.StartsWith("Provider ", Assert.Single(document.ProviderNames));
        Assert.Equal("Type2 AC CCS2 DC", document.ConnectorText);
    }

    [Fact]
    public async Task Handle_RedeliveredEvent_LeavesOneUnchangedDocument()
    {
        var station = await database.SeedStationAsync();

        await search.IndexStationAsync(database, station.Id);
        var first = await search.GetStationAsync(station.PublicId);
        await search.IndexStationAsync(database, station.Id);
        var second = await search.GetStationAsync(station.PublicId);

        Assert.Equivalent(first, second);
    }

    [Fact]
    public async Task Handle_EventAfterNewerEdit_IndexesTheCurrentState()
    {
        var station = await database.SeedStationAsync();
        await database.RenameStationAsync(station.Id, "First name");
        await database.RenameStationAsync(station.Id, "Second name");

        // The event raised by the first edit is handled after the second edit committed.
        await search.IndexStationAsync(database, station.Id);

        Assert.Equal("Second name", (await search.GetStationAsync(station.PublicId))!.Name);
    }

    [Fact]
    public async Task Handle_EventWithNoIndex_RequestsABuildInsteadOfWriting()
    {
        var station = await database.SeedStationAsync();
        await search.DeleteStationIndicesAsync();

        Assert.NotNull(await search.HandleEventAsync(database, station.Id));
        Assert.Empty(await search.IndicesBehindAsync(StationSearchIndex.Alias));
    }

    [Fact]
    public async Task Handle_UnreachableElasticsearch_ThrowsUnavailableAndDoesNotSucceed()
    {
        var station = await database.SeedStationAsync();
        var unreachable = new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri("http://127.0.0.1:1")).MaximumRetries(0));

        await Assert.ThrowsAsync<SearchIndexUnavailableException>(
            () => search.IndexStationAsync(database, station.Id, new StationSearchIndexWriter(unreachable)));
    }
}
