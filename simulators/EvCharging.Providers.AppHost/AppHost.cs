using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres(
    "synchronous-provider-postgres",
    userName: builder.AddParameter("synchronous-provider-username", "synchronous_provider"),
    password: PersistentSecret("synchronous-provider-password"))
    .WithImageTag("18.6-bookworm")
    .WithDataVolume()
    .AddDatabase("synchronous-provider", "synchronous_charging");

var identity = builder.AddKeycloak(
    "provider-identity", adminPassword: PersistentSecret("provider-identity-admin-password"))
    .WithImageTag("26.8.0")
    .WithDataVolume()
    .WithRealmImport("../../infra/keycloak/providers/providers-realm.json")
    .WithEnvironment("CHARGING_CLIENT_SECRET", PersistentSecret("charging-client-secret"))
    .WithEnvironment("SIMULATION_CLIENT_SECRET", PersistentSecret("simulation-client-secret"));

var migrations = builder.AddProject<Projects.EvCharging_SynchronousChargingProviderApi_Migrations>(
    "synchronous-provider-migrations", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(database)
    .WaitFor(database);

var synchronousApi = builder.AddProject<Projects.EvCharging_SynchronousChargingProviderApi>(
    "synchronous-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(database)
    .WaitFor(database)
    .WaitForCompletion(migrations)
    .WithReference(identity)
    .WaitFor(identity)
    .WithEnvironment(
        "ProviderAuthentication__Authority",
        ReferenceExpression.Create($"{identity.GetEndpoint("http")}/realms/providers"))
    .WithEnvironment("ProviderAuthentication__Audience", "synchronous-charging-provider")
    .WithEnvironment("ProviderAuthentication__AllowLocalHttp", builder.ExecutionContext.IsRunMode ? "true" : "false")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

var queuedApi = builder.AddProject<Projects.EvCharging_QueuedChargingProviderApi>(
    "queued-charging-provider-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

if (builder.Environment.IsDevelopment())
{
    foreach (var httpApi in new[] { synchronousApi, queuedApi })
    {
        httpApi.WithUrlForEndpoint(
            "http",
            url =>
            {
                url.Url = "/scalar";
                url.DisplayText = "Scalar";
            });
    }
}

builder.Build().Run();

IResourceBuilder<ParameterResource> PersistentSecret(string name) =>
    builder.AddParameter(
        name,
        new GenerateParameterDefault { MinLength = 32, Special = false },
        secret: true,
        persist: true);
