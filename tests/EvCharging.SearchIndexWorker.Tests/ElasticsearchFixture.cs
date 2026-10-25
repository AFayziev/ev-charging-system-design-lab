using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Transport;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Persistence;
using EvCharging.SearchIndex;
using EvCharging.SearchIndexWorker.Handlers;
using EvCharging.SearchIndexWorker.Indexing;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvCharging.SearchIndexWorker.Tests;

public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private const int HttpPort = 9200;
    private readonly IContainer _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/_cluster/health")))
        .Build();

    public ElasticsearchClient Client { get; private set; } = null!;

    private Uri Address => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}");

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _container.StartAsync(timeout.Token);
        Client = new ElasticsearchClient(new ElasticsearchClientSettings(Address));
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    // A client whose writes take a random time, so concurrent handlers of one station overlap.
    public ElasticsearchClient CreateSlowWriteClient() => new(new ElasticsearchClientSettings(
        new SingleNodePool(Address),
        new HttpRequestInvoker((handler, _) => new DelayingHandler(handler))));

    public ElasticsearchClient CreateClient(IndexAliasSwapGate gate) => new(new ElasticsearchClientSettings(
        new SingleNodePool(Address),
        new HttpRequestInvoker((handler, _) => gate.Wrap(handler))));

    // Runs the station-changed handler as the search worker does: its own database role, reading the committed row.
    // The command it returns when no index exists is handled as the worker does, then the event is handled again.
    public async Task IndexStationAsync(
        PlatformDatabaseFixture database, Guid stationId, StationSearchIndexWriter? writer = null)
    {
        if (await HandleEventAsync(database, stationId, writer) is not null)
        {
            await BuildIndexAsync(database);
            Assert.Null(await HandleEventAsync(database, stationId, writer));
        }
    }

    public async Task<EnsureStationSearchIndexCommand?> HandleEventAsync(
        PlatformDatabaseFixture database, Guid stationId, StationSearchIndexWriter? writer = null)
    {
        await using var workerDatabase = CreateWorkerDatabase(database);
        return await StationChangedEventHandler.HandleAsync(
            new StationChangedEvent(stationId),
            workerDatabase,
            writer ?? new StationSearchIndexWriter(Client),
            CancellationToken.None);
    }

    public async Task BuildIndexAsync(PlatformDatabaseFixture database, ElasticsearchClient? client = null)
    {
        await using var workerDatabase = CreateWorkerDatabase(database);
        await new StationSearchIndexBuilder(client ?? Client).EnsureAsync(
            workerDatabase, Guid.NewGuid(), CancellationToken.None);
    }

    public async Task<IReadOnlyList<string>> IndicesBehindAsync(string alias)
    {
        var response = await Client.Indices.GetAliasAsync(request => request.Name(alias));
        return response.IsValidResponse
            ? response.Aliases!.Keys.Select(name => name.ToString()).ToList()
            : [];
    }

    public async Task DeleteStationIndicesAsync()
    {
        // Wildcard deletes are refused by default, so each index is deleted by name.
        var indices = await Client.Indices.GetAsync(StationSearchIndex.IndexPattern);
        foreach (var index in indices.Indices.Keys)
        {
            await Client.Indices.DeleteAsync(index);
        }
    }

    private static PlatformDbContext CreateWorkerDatabase(PlatformDatabaseFixture database) => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(database.GetDataSource("search_messaging"))
            .Options);

    public async Task<StationSearchDocument?> GetStationAsync(Guid publicId)
    {
        var response = await Client.GetAsync<StationSearchDocument>(StationSearchIndex.Alias, publicId);
        return response.Found ? response.Source : null;
    }

    public async Task<StationSearchDocument> WaitForStationAsync(
        Guid publicId, Func<StationSearchDocument, bool> condition, TimeSpan timeout)
    {
        StationSearchDocument? document = null;
        await Polling.WaitUntilAsync(
            async () =>
            {
                document = await GetStationAsync(publicId);
                return document is not null && condition(document);
            },
            timeout);
        return document!;
    }

    public async Task SetWriteBlockAsync(bool blocked)
    {
        var response = await Client.Indices.PutSettingsAsync(
            new PutIndicesSettingsRequest
            {
                Indices = StationSearchIndex.Alias,
                Settings = new IndexSettings { Blocks = new IndexSettingBlocks { Write = blocked } }
            });
        Assert.True(response.IsValidResponse, response.DebugInformation);
    }
}
