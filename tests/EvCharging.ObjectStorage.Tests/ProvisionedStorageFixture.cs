using System.Globalization;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EvCharging.ObjectStorage.Tests;

/// <summary>
/// RustFS provisioned by the real <c>infra/rustfs/platform</c> image, so the tests run through the same buckets,
/// access rules, and per-host users the platform uses.
/// </summary>
public sealed class ProvisionedStorageFixture : IAsyncLifetime
{
    public const string PrivateBucket = "private-files";
    public const string PublicBucket = "public-media";
    public const string WebOrigin = "http://localhost:5173";
    public const int UploadExpiryDays = 1;
    private const string NetworkAlias = "storage";
    private const string RootAccessKey = "root-access-key-for-tests";
    private const string RootSecretKey = "root-secret-key-for-tests";
    private const string ApiFilesSecretKey = "api-files-secret-for-tests";
    private const string MediaWorkerSecretKey = "media-worker-secret-for-tests";
    private const int ServicePort = 9000;
    private const string CliImage = "docker.io/rustfs/rc:v0.1.36";
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly IFutureDockerImage _provisioningImage = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), "infra/rustfs/platform")
        .WithBuildArgument("RUSTFS_CLI_IMAGE", CliImage)
        .WithName($"object-storage-provisioning-tests:{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();
    private readonly IContainer _storage;
    private readonly List<IHost> _hosts = [];

    public ProvisionedStorageFixture() =>
        _storage = new ContainerBuilder("rustfs/rustfs:1.0.1")
            .WithNetwork(_network)
            .WithNetworkAliases(NetworkAlias)
            .WithEnvironment("RUSTFS_ACCESS_KEY", RootAccessKey)
            .WithEnvironment("RUSTFS_SECRET_KEY", RootSecretKey)
            .WithEnvironment("RUSTFS_VOLUMES", "/data")
            .WithEnvironment("RUSTFS_ADDRESS", "0.0.0.0:" + ServicePort)
            .WithPortBinding(ServicePort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(ServicePort).ForPath("/health")))
            .Build();

    public Uri ServiceUrl => new($"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(ServicePort)}");

    /// <summary>The store as the API sees it, through its <c>api-files</c> user.</summary>
    public ObjectStore ApiStore { get; private set; } = null!;

    /// <summary>The store as the media worker sees it, through its <c>media-worker</c> user.</summary>
    public ObjectStore MediaWorkerStore { get; private set; } = null!;

    /// <summary>A raw client with the API's credentials, for calls outside the store's surface.</summary>
    public IAmazonS3 ApiClient { get; private set; } = null!;

    /// <summary>A raw client with root credentials, for reading back provisioned configuration.</summary>
    public IAmazonS3 RootClient { get; private set; } = null!;

    /// <summary>An anonymous HTTP client standing in for the browser.</summary>
    public HttpClient Browser { get; } = new();

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await _network.CreateAsync(timeout.Token);
        await Task.WhenAll(
            _provisioningImage.CreateAsync(timeout.Token),
            _storage.StartAsync(timeout.Token));

        await using var provisioning = new ContainerBuilder(_provisioningImage)
            .WithNetwork(_network)
            .WithEnvironment("STORAGE_URL", $"http://{NetworkAlias}:{ServicePort}")
            .WithEnvironment("ROOT_ACCESS_KEY", RootAccessKey)
            .WithEnvironment("ROOT_SECRET_KEY", RootSecretKey)
            .WithEnvironment("PRIVATE_BUCKET", PrivateBucket)
            .WithEnvironment("PUBLIC_BUCKET", PublicBucket)
            .WithEnvironment("WEB_ORIGIN", WebOrigin)
            .WithEnvironment("UPLOAD_EXPIRY_DAYS", UploadExpiryDays.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment("API_FILES_SECRET_KEY", ApiFilesSecretKey)
            .WithEnvironment("MEDIA_WORKER_SECRET_KEY", MediaWorkerSecretKey)
            .Build();
        await provisioning.StartAsync(timeout.Token);
        var exitCode = await provisioning.GetExitCodeAsync(timeout.Token);
        var (_, stderr) = await provisioning.GetLogsAsync(ct: timeout.Token);
        Assert.True(exitCode == 0, stderr);

        var api = await StartStorageAsync("api-files", ApiFilesSecretKey, timeout.Token);
        var mediaWorker = await StartStorageAsync("media-worker", MediaWorkerSecretKey, timeout.Token);
        var root = await StartStorageAsync(RootAccessKey, RootSecretKey, timeout.Token);
        ApiStore = api.GetRequiredService<ObjectStore>();
        MediaWorkerStore = mediaWorker.GetRequiredService<ObjectStore>();
        ApiClient = api.GetRequiredService<ObjectStorageClients>().Requests;
        RootClient = root.GetRequiredService<ObjectStorageClients>().Requests;
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }

        Browser.Dispose();
        await _storage.DisposeAsync();
        await _provisioningImage.DisposeAsync();
        await _network.DisposeAsync();
    }

    // Built through the platform registration, so the tests cover the client configuration hosts run with.
    private async Task<IServiceProvider> StartStorageAsync(
        string accessKey, string secretKey, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ObjectStorage:ServiceUrl"] = ServiceUrl.ToString(),
            ["ObjectStorage:AccessKey"] = accessKey,
            ["ObjectStorage:SecretKey"] = secretKey,
            ["ObjectStorage:PrivateBucket"] = PrivateBucket,
            ["ObjectStorage:PublicBucket"] = PublicBucket,
            ["ObjectStorage:PublicBaseUrl"] = new Uri(ServiceUrl, PublicBucket).ToString()
        });
        builder.AddObjectStorage();
        var host = builder.Build();
        _hosts.Add(host);
        await host.StartAsync(cancellationToken);
        return host.Services;
    }
}
