using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using EvCharging.SearchIndex;
using EvCharging.Testing;
using Xunit;

namespace EvCharging.SearchApi.Tests;

/// <summary>
/// Elasticsearch with security on, provisioned by the real <c>provision.sh</c> and role definition, so the tests
/// run through the same read-only user the platform uses.
/// </summary>
public sealed class SecuredSearchIndexFixture : IAsyncLifetime
{
    public const string ReaderUser = "search_reader";
    public const string ReaderPassword = "reader-password-for-tests";
    private const string AdminPassword = "admin-password-for-tests";
    private const int HttpPort = 9200;
    private readonly IContainer _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.http.ssl.enabled", "false")
        .WithEnvironment("xpack.security.transport.ssl.enabled", "false")
        .WithEnvironment("ELASTIC_PASSWORD", AdminPassword)
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "provisioning"), "/provisioning/")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(HttpPort))
        .Build();

    public ElasticsearchClient AdminClient { get; private set; } = null!;

    public ElasticsearchClient ReaderClient { get; private set; } = null!;

    public Uri Address => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}");

    public string ReaderConnectionString => $"http://{ReaderUser}:{ReaderPassword}@{Address.Authority}";

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _container.StartAsync(timeout.Token);
        AdminClient = CreateClient("elastic", AdminPassword);
        ReaderClient = CreateClient(ReaderUser, ReaderPassword);

        // Provisioning expects a ready index, as the AppHost guarantees by waiting for it.
        await Polling.WaitUntilAsync(
            async () => (await AdminClient.Cluster.HealthAsync(timeout.Token)).IsValidResponse,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(1));

        var provisioning = await _container.ExecAsync(
            [
                "env",
                $"ELASTICSEARCH_URL=http://localhost:{HttpPort}",
                $"ELASTIC_PASSWORD={AdminPassword}",
                $"SEARCH_READER_PASSWORD={ReaderPassword}",
                "sh",
                "/provisioning/provision.sh"
            ],
            timeout.Token);
        Assert.True(provisioning.ExitCode == 0, provisioning.Stderr);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Replaces the index with these stations behind the <c>stations</c> alias and an empty rebuild alias.</summary>
    public async Task ResetIndexAsync(params StationSearchDocument[] stations)
    {
        await DeleteStationIndicesAsync();
        var index = $"stations-{Guid.NewGuid():N}";
        var created = await AdminClient.Indices.CreateAsync(
            index,
            request => request
                .Aliases(aliases => aliases.Add(StationSearchIndex.Alias, _ => { }))
                .Mappings(mappings => mappings.Properties(StationSearchIndex.StationProperties())));
        Assert.True(created.IsValidResponse, created.DebugInformation);

        var rebuild = await AdminClient.Indices.CreateAsync(
            $"stations-{Guid.NewGuid():N}",
            request => request.Aliases(aliases => aliases.Add(StationSearchIndex.RebuildAlias, _ => { })));
        Assert.True(rebuild.IsValidResponse, rebuild.DebugInformation);

        if (stations.Length > 0)
        {
            var indexed = await AdminClient.BulkAsync(
                bulk => bulk.Index(StationSearchIndex.Alias).IndexMany(
                    stations,
                    (operation, station) => operation.Id(station.PublicId.ToString()))
                    .Refresh(Refresh.True));
            Assert.True(
                indexed.IsValidResponse,
                string.Join(
                    "; ",
                    indexed.ItemsWithErrors.Select(item => item.Error?.Reason ?? item.Status.ToString())));
        }
    }

    /// <summary>Wildcard deletes are refused by default, so the indices are deleted by name.</summary>
    public async Task DeleteStationIndicesAsync()
    {
        var existing = await AdminClient.Indices.GetAsync(StationSearchIndex.IndexPattern);
        if (existing.Indices.Count > 0)
        {
            var deleted = await AdminClient.Indices.DeleteAsync(Indices.Parse(string.Join(',', existing.Indices.Keys)));
            Assert.True(deleted.IsValidResponse, deleted.DebugInformation);
        }
    }

    private ElasticsearchClient CreateClient(string user, string password) => new(
        new ElasticsearchClientSettings(Address).Authentication(new BasicAuthentication(user, password)));
}
