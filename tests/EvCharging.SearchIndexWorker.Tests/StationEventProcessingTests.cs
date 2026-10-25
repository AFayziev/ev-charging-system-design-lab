using Elastic.Clients.Elasticsearch;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Persistence;
using EvCharging.SearchIndexWorker.Configuration;
using EvCharging.SearchIndexWorker.Handlers;
using EvCharging.SearchIndexWorker.Indexing;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Xunit;

namespace EvCharging.SearchIndexWorker.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class StationEventProcessingTests(ElasticsearchFixture search, PlatformDatabaseFixture database)
{
    private const string Queue = "search-station-events-test";
    private const string IndexBuildQueue = "search-index-build-test";

    [Fact]
    public async Task Events_BackToBackEditsWithSlowWrites_EndAtTheNewestState()
    {
        var station = await database.SeedStationAsync();
        using var host = await StartHostAsync(search.CreateSlowWriteClient());
        var bus = host.Services.GetRequiredService<IMessageBus>();

        // The second event starts while the first is still writing, so without per-station ordering the older
        // read could finish last and overwrite the newer one.
        for (int round = 1; round <= 40; round++)
        {
            await database.RenameStationAsync(station.Id, $"First {round}");
            await PublishAsync(bus, station.Id);
            await Task.Delay(TimeSpan.FromMilliseconds(3));
            await database.RenameStationAsync(station.Id, $"Second {round}");
            await PublishAsync(bus, station.Id);

            await search.WaitForStationAsync(
                station.PublicId, value => value.Name == $"Second {round}", TimeSpan.FromSeconds(30));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.Equal($"Second {round}", (await search.GetStationAsync(station.PublicId))!.Name);
        }
    }

    [Fact]
    public async Task Events_WithNoIndex_BuildTheIndexAndIndexTheStation()
    {
        var station = await database.SeedStationAsync();
        await search.DeleteStationIndicesAsync();
        using var host = await StartHostAsync();

        await PublishAsync(host.Services.GetRequiredService<IMessageBus>(), station.Id);

        await search.WaitForStationAsync(
            station.PublicId,
            value => value.Name == "Search test",
            TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Events_ElasticsearchBlocked_AreIndexedAfterRestartAndRecovery()
    {
        var station = await database.SeedStationAsync();
        await search.IndexStationAsync(database, station.Id);
        await database.RenameStationAsync(station.Id, "Edited during outage");
        await search.SetWriteBlockAsync(true);
        try
        {
            using (var first = await StartHostAsync())
            {
                await PublishAsync(first.Services.GetRequiredService<IMessageBus>(), station.Id);
                await Task.Delay(TimeSpan.FromSeconds(2));
                Assert.Equal("Search test", (await search.GetStationAsync(station.PublicId))!.Name);
                await first.StopAsync();
            }

            using var restarted = await StartHostAsync();
            await search.SetWriteBlockAsync(false);

            await search.WaitForStationAsync(
                station.PublicId, value => value.Name == "Edited during outage", TimeSpan.FromSeconds(60));
        }
        finally
        {
            await search.SetWriteBlockAsync(false);
        }
    }

    private static ValueTask PublishAsync(IMessageBus bus, Guid stationId) => bus.PublishAsync(
        new StationChangedEvent(stationId), new DeliveryOptions { GroupId = stationId.ToString() });

    // The search worker's handler, retry policy, and per-station partitioning, over a durable local queue.
    private async Task<IHost> StartHostAsync(ElasticsearchClient? client = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<PlatformDbContext>(
            options => options.UseNpgsql(database.GetDataSource("search_messaging")));
        builder.Services.AddSingleton(client ?? search.Client);
        builder.Services.AddSingleton<StationSearchIndexWriter>();
        builder.Services.AddSingleton(
            provider => new StationSearchIndexBuilder(provider.GetRequiredService<ElasticsearchClient>()));
        builder.UseWolverine(options =>
        {
            options.UseEntityFrameworkCoreTransactions();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(StationChangedEventHandler));
            options.Discovery.IncludeType(typeof(EnsureStationSearchIndexCommandHandler));
            options.PersistMessagesWithPostgresql(database.GetConnectionString("platform_migrator"), "search_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Policies.UseDurableLocalQueues();
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
            options.AddSearchFailurePolicies([TimeSpan.FromMilliseconds(200)]);
            options.PublishMessage<StationChangedEvent>().ToLocalQueue(Queue);
            options.PublishMessage<EnsureStationSearchIndexCommand>().ToLocalQueue(IndexBuildQueue);
            options.LocalQueue(IndexBuildQueue).Sequential();
            options.LocalQueue(Queue).PartitionProcessingByGroupId(PartitionSlots.Five);
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
