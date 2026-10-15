var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EvCharging_SynchronousChargingProviderApi>(
    "synchronous-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.EvCharging_QueuedChargingProviderApi>(
    "queued-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
