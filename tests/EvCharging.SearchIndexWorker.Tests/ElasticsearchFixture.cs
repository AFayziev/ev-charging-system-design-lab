using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Transport;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Persistence;
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

    // Runs the station-changed handler as the search worker does: its own database role, reading the committed row.
    public async Task IndexStationAsync(
        PlatformDatabaseFixture database, Guid stationId, StationSearchIndexWriter? writer = null)
    {
        await using var workerDatabase = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(database.GetDataSource("search_messaging"))
                .Options);
        writer ??= new StationSearchIndexWriter(Client);
        await StationChangedEventHandler.HandleAsync(
            new StationChangedEvent(stationId), workerDatabase, writer, CancellationToken.None);
    }

    public async Task<StationSearchDocument?> GetStationAsync(Guid publicId)
    {
        var response = await Client.GetAsync<StationSearchDocument>(StationSearchIndexWriter.Alias, publicId);
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
                Indices = StationSearchIndexWriter.Alias,
                Settings = new IndexSettings { Blocks = new IndexSettingBlocks { Write = blocked } }
            });
        Assert.True(response.IsValidResponse, response.DebugInformation);
    }
}
