var builder = DistributedApplication.CreateBuilder(args);

var apiCount = await InstanceCount("api-instances");
var gatewayCount = await InstanceCount("gateway-instances");
var api = builder.AddProject<Projects.EvCharging_Api>("api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithReplicas(apiCount)
    .WithHttpHealthCheck("/health");

var gateway = builder.AddProject<Projects.EvCharging_Gateway>("gateway")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithReplicas(gatewayCount)
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

builder.AddViteApp("frontend", "../EvCharging.Web")
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

var synchronousChargingProviderApi = builder.AddProject<Projects.EvCharging_SynchronousChargingProviderApi>("synchronous-charging-provider-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");
var queuedChargingProviderApi = builder.AddProject<Projects.EvCharging_QueuedChargingProviderApi>("queued-charging-provider-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");
var notificationProviderApi = builder.AddProject<Projects.EvCharging_NotificationProviderApi>("notification-provider-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.EvCharging_ChargingWorker>("charging-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(gateway)
    .WithReference(synchronousChargingProviderApi)
    .WithReference(queuedChargingProviderApi)
    .WaitFor(gateway)
    .WaitFor(synchronousChargingProviderApi)
    .WaitFor(queuedChargingProviderApi);

builder.AddProject<Projects.EvCharging_ObservationWorker>("observation-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.AddProject<Projects.EvCharging_SearchIndexWorker>("search-index-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.AddProject<Projects.EvCharging_CacheWorker>("cache-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.AddProject<Projects.EvCharging_MediaWorker>("media-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.AddProject<Projects.EvCharging_NotificationWorker>("notification-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(notificationProviderApi)
    .WaitFor(notificationProviderApi);

builder.Build().Run();

async Task<int> InstanceCount(string name)
{
    var parameter = builder.AddParameter(name, () => builder.Configuration[$"Parameters:{name}"] ?? "2");
    if (!int.TryParse(await parameter.Resource.GetValueAsync(CancellationToken.None), out var count) || count is < 1 or > 8)
    {
        throw new InvalidOperationException($"Parameters:{name} must be an integer between 1 and 8.");
    }
    return count;
}
