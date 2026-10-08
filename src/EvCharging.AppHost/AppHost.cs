using EvCharging.AppHost;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPlatformDatabase();
var authentication = builder.AddAuthenticationDatabase(database.Direct);
var authenticationMigrations = builder.AddProject<Projects.EvCharging_Gateway_Migrations>("gateway-auth-migrations", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(authentication.Migration)
    .WaitForCompletion(authentication.Bootstrap);

var gatewayOidcSecret = builder.AddPersistentSecret("gateway-oidc-secret");
var identity = builder.AddIdentityProvider(gatewayOidcSecret);

var documents = builder.AddDocumentDatabase();
var cacheStore = builder.AddCache();
var broker = builder.AddMessageBroker();
var stream = builder.AddObservationStream();
var searchIndex = builder.AddSearchIndex();
var (storage, storageAccessKey, storageSecretKey) = builder.AddObjectStorage();

var apiCount = await InstanceCountAsync("api-instances");
var gatewayCount = await InstanceCountAsync("gateway-instances");
var api = builder.AddProject<Projects.EvCharging_Api>("api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithReplicas(apiCount)
    .WithHttpHealthCheck("/health");

api.WithObjectStorage(storage, storageAccessKey, storageSecretKey)
    .WithReference(searchIndex).WaitFor(searchIndex)
    .WithReference(database.Write).WithReference(database.Read).WithReference(database.Direct)
    .WaitFor(database.Direct)
    .WithReference(documents).WaitFor(documents)
    .WithReference(cacheStore).WaitFor(cacheStore)
    .WithReference(broker).WaitFor(broker)
    .WithReference(stream).WaitFor(stream);

var gateway = builder.AddProject<Projects.EvCharging_Gateway>("gateway", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode ? builder.Configuration.GetValue("Gateway:HttpPort", 5100) : null)
    .WithReplicas(gatewayCount)
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

gateway.WithReference(authentication.Runtime).WaitForCompletion(authenticationMigrations)
    .WithReference(identity.GetEndpoint("http")).WaitFor(identity);

var gatewayPublicOrigin = builder.ExecutionContext.IsRunMode && builder.Configuration["Parameters:gateway-public-origin"] is null
    ? ReferenceExpression.Create($"http://localhost:{gateway.GetEndpoint("http").Property(EndpointProperty.Port)}")
    : ReferenceExpression.Create($"{builder.AddParameter("gateway-public-origin")}");
identity.WithEnvironment("GATEWAY_PUBLIC_ORIGIN", gatewayPublicOrigin);

builder.AddViteApp("frontend", "../EvCharging.Web")
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

var synchronousChargingProviderApi = builder.AddProject<Projects.EvCharging_SynchronousChargingProviderApi>(
    "synchronous-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");
var queuedChargingProviderApi = builder.AddProject<Projects.EvCharging_QueuedChargingProviderApi>(
    "queued-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");
var notificationProviderApi = builder.AddProject<Projects.EvCharging_NotificationProviderApi>(
    "notification-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

var charging = builder.AddProject<Projects.EvCharging_ChargingWorker>("charging-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(gateway)
    .WithReference(synchronousChargingProviderApi)
    .WithReference(queuedChargingProviderApi)
    .WaitFor(gateway)
    .WaitFor(synchronousChargingProviderApi)
    .WaitFor(queuedChargingProviderApi);

var observation = builder.AddProject<Projects.EvCharging_ObservationWorker>("observation-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

var search = builder.AddProject<Projects.EvCharging_SearchIndexWorker>("search-index-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

var cache = builder.AddProject<Projects.EvCharging_CacheWorker>("cache-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

var media = builder.AddProject<Projects.EvCharging_MediaWorker>("media-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

var notification = builder.AddProject<Projects.EvCharging_NotificationWorker>("notification-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(notificationProviderApi)
    .WaitFor(notificationProviderApi);

foreach (var worker in new[] { charging, observation, search, cache, media, notification })
{
    worker.WithReference(database.Write).WaitFor(database.Direct)
        .WithReference(broker).WaitFor(broker);
}

foreach (var consumer in new[] { observation, search, cache })
{
    consumer.WithReference(stream).WaitFor(stream);
}

observation.WithReference(documents).WaitFor(documents);
foreach (var reader in new[] { search, cache })
{
    reader.WithReference(database.Read);
}

cache.WithReference(cacheStore).WaitFor(cacheStore);
search.WithReference(searchIndex).WaitFor(searchIndex);

media.WithObjectStorage(storage, storageAccessKey, storageSecretKey)
    .WithReference(documents).WaitFor(documents);
synchronousChargingProviderApi.WithReference(gateway).WaitFor(gateway);
queuedChargingProviderApi.WithReference(gateway).WaitFor(gateway);

builder.Build().Run();

async Task<int> InstanceCountAsync(string name)
{
    var parameter = builder.AddParameter(name, () => builder.Configuration[$"Parameters:{name}"] ?? "2");
    if (!int.TryParse(await parameter.Resource.GetValueAsync(CancellationToken.None), out var count) || count is < 1 or > 8)
    {
        throw new InvalidOperationException($"Parameters:{name} must be an integer between 1 and 8.");
    }

    return count;
}
