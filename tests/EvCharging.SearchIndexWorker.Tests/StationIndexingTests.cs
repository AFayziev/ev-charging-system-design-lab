using Elastic.Clients.Elasticsearch;
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
    public async Task Index_AfterTheIndexIsDeleted_FailsOnceThenRecreatesTheIndexWithTheAlias()
    {
        var station = await database.SeedStationAsync();
        var writer = new StationSearchIndexWriter(search.Client);
        await search.IndexStationAsync(database, station.Id, writer);
        await search.Client.Indices.DeleteAsync(StationSearchIndexWriter.InitialIndex);

        await Assert.ThrowsAsync<SearchIndexUnavailableException>(
            () => search.IndexStationAsync(database, station.Id, writer));
        await search.IndexStationAsync(database, station.Id, writer);

        var aliases = await search.Client.Indices.GetAliasAsync(
            request => request.Name(StationSearchIndexWriter.Alias));
        Assert.Equal([StationSearchIndexWriter.InitialIndex], aliases.Aliases!.Keys.Select(name => name.ToString()));
        Assert.NotNull(await search.GetStationAsync(station.PublicId));
    }

    [Fact]
    public async Task Index_UnreachableElasticsearch_ThrowsUnavailableAndDoesNotSucceed()
    {
        var station = await database.SeedStationAsync();
        var unreachable = new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri("http://127.0.0.1:1")).MaximumRetries(0));

        await Assert.ThrowsAsync<SearchIndexUnavailableException>(
            () => search.IndexStationAsync(database, station.Id, new StationSearchIndexWriter(unreachable)));
    }
}
