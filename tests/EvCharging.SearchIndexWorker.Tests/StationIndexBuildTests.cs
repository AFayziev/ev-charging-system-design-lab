using EvCharging.SearchIndex;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvCharging.SearchIndexWorker.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class StationIndexBuildTests(ElasticsearchFixture search, PlatformDatabaseFixture database)
{
    [Fact]
    public async Task Build_FromEmpty_IndexesEveryStationBehindOneIndex()
    {
        await database.SeedStationAsync();
        await database.SeedStationAsync();
        await search.DeleteStationIndicesAsync();

        await search.BuildIndexAsync(database);

        Assert.Single(await search.IndicesBehindAsync(StationSearchIndex.Alias));
        Assert.Empty(await search.IndicesBehindAsync(StationSearchIndex.RebuildAlias));
        Assert.Equal(await PublicIdsInDatabaseAsync(), await PublicIdsInIndexAsync());
        var mapping = await search.Client.Indices.GetMappingAsync(StationSearchIndex.Alias);
        Assert.Equal(
            StationSearchIndex.MappingVersion,
            mapping.Mappings.Values.Single().Mappings.Meta![StationSearchIndex.MappingVersionKey].ToString());
    }

    [Fact]
    public async Task Build_WhenTheIndexIsCurrent_ChangesNothing()
    {
        await search.DeleteStationIndicesAsync();
        await search.BuildIndexAsync(database);
        var before = await search.IndicesBehindAsync(StationSearchIndex.Alias);

        await search.BuildIndexAsync(database);

        Assert.Equal(before, await search.IndicesBehindAsync(StationSearchIndex.Alias));
    }

    [Fact]
    public async Task Build_AfterAnInterruptedBuild_RemovesTheLeftoverIndex()
    {
        await search.DeleteStationIndicesAsync();
        await search.BuildIndexAsync(database);
        await search.Client.Indices.CreateAsync(
            "stations-interrupted",
            request => request.Aliases(aliases => aliases.Add(StationSearchIndex.RebuildAlias, _ => { })));

        await search.BuildIndexAsync(database);

        Assert.Single(await search.IndicesBehindAsync(StationSearchIndex.Alias));
        Assert.Empty(await search.IndicesBehindAsync(StationSearchIndex.RebuildAlias));
        Assert.False((await search.Client.Indices.ExistsAsync("stations-interrupted")).Exists);
    }

    [Fact]
    public async Task Build_EditAfterTheLoadBeforeTheSwap_KeepsTheEdit()
    {
        var station = await database.SeedStationAsync();
        await search.DeleteStationIndicesAsync();
        var gate = new IndexAliasSwapGate();
        var build = Task.Run(() => search.BuildIndexAsync(database, search.CreateClient(gate)));
        await gate.Reached;

        // The load has read the station already, so only the live write can carry this edit into the new index.
        await database.RenameStationAsync(station.Id, "Edited during the build");
        await search.HandleEventAsync(database, station.Id);
        gate.Release();
        await build;

        Assert.Equal("Edited during the build", (await search.GetStationAsync(station.PublicId))!.Name);
    }

    private async Task<List<Guid>> PublicIdsInDatabaseAsync()
    {
        await using var context = database.CreateContext();
        return await context.Stations.Select(station => station.PublicId).OrderBy(id => id).ToListAsync();
    }

    private async Task<List<Guid>> PublicIdsInIndexAsync()
    {
        await search.Client.Indices.RefreshAsync(StationSearchIndex.Alias);
        var response = await search.Client.SearchAsync<StationSearchDocument>(
            request => request.Indices(StationSearchIndex.Alias).Size(10_000));
        return response.Documents.Select(document => document.PublicId).OrderBy(id => id).ToList();
    }
}
