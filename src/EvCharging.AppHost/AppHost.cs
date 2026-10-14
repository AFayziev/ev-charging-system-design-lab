using EvCharging.AppHost;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPlatformDatabase();
var databaseAccess = builder.AddDatabaseAccess(database);
var authenticationMigrations = builder.AddProject<Projects.EvCharging_Gateway_Migrations>(
    "gateway-auth-migrations",
    launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(databaseAccess.AuthenticationMigration)
    .WaitForCompletion(databaseAccess.Provisioning);
var platformMigrations = builder.AddProject<Projects.EvCharging_Persistence_Migrations>(
    "platform-migrations",
    launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(databaseAccess.Migration)
    .WaitForCompletion(databaseAccess.Provisioning);
var apiMessageMigrations = builder.AddMessageStorageMigration(
    "api", databaseAccess.Migration, databaseAccess.Provisioning);
var chargingMessageMigrations = builder.AddMessageStorageMigration(
    "charging", databaseAccess.Migration, databaseAccess.Provisioning);
var notificationMessageMigrations = builder.AddMessageStorageMigration(
    "notification", databaseAccess.Migration, databaseAccess.Provisioning);

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
    .WithReference(searchIndex)
    .WaitFor(searchIndex)
    .WithReference(databaseAccess.Write)
    .WithReference(databaseAccess.Read)
    .WithReference(databaseAccess.Write, "messaging")
    .WaitForCompletion(platformMigrations)
    .WaitForCompletion(apiMessageMigrations)
    .WithReference(documents)
    .WaitFor(documents)
    .WithReference(cacheStore)
    .WaitFor(cacheStore)
    .WithReference(broker)
    .WaitFor(broker)
    .WithReference(stream)
    .WaitFor(stream);

var gateway = builder.AddProject<Projects.EvCharging_Gateway>("gateway", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode
        ? builder.Configuration.GetValue("Gateway:HttpPort", 5100) : null)
    .WithReplicas(gatewayCount)
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

gateway.WithReference(databaseAccess.Authentication)
    .WaitForCompletion(authenticationMigrations)
    .WithReference(identity.GetEndpoint("http"))
    .WaitFor(identity);

var frontendDiagnosticsSettings = builder.Configuration.GetSection("FrontendDiagnostics")
    .AsEnumerable()
    .Where(setting => setting.Value is not null);

foreach (var setting in frontendDiagnosticsSettings)
{
    gateway.WithEnvironment(setting.Key.Replace(":", "__", StringComparison.Ordinal), setting.Value!);
}

var frontend = builder.AddViteApp("frontend", "../EvCharging.Web")
    .WithEndpoint(
        "http",
        endpoint => endpoint.Port = builder.ExecutionContext.IsRunMode
            ? builder.Configuration.GetValue("Frontend:HttpPort", 5173) : null)
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

var gatewayPublicOrigin = builder.AddBrowserOrigin(frontend.GetEndpoint("http"), "gateway-public-origin");
var identityPublicOrigin = builder.AddBrowserOrigin(identity.GetEndpoint("http"), "identity-public-origin");
identity.WithEnvironment("GATEWAY_PUBLIC_ORIGIN", gatewayPublicOrigin)
    .WithEnvironment("KC_HOSTNAME", identityPublicOrigin)
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");

var identityAuthority = ReferenceExpression.Create($"{identityPublicOrigin}/realms/evcharging");
const string IdentityMetadataAddress = "http://keycloak/realms/evcharging/.well-known/openid-configuration";

platformMigrations.WithEnvironment("LabData__Enabled", "true")
    .WithEnvironment("LabData__IdentityIssuer", identityAuthority);

if (builder.ExecutionContext.IsRunMode)
{
    builder.AddProject<Projects.EvCharging_Persistence_Migrations>("lab-data-reset", launchProfileName: null)
        .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
        .WithEnvironment("LabData__Enabled", "true")
        .WithEnvironment("LabData__Reset", "true")
        .WithEnvironment("LabData__IdentityIssuer", identityAuthority)
        .WithReference(databaseAccess.Migration)
        .WaitForCompletion(platformMigrations)
        .WithExplicitStart()
        .ExcludeFromManifest();
}

api.WithReference(identity.GetEndpoint("http"))
    .WaitFor(identity)
    .WithEnvironment("ApiAuthentication__Authority", identityAuthority)
    .WithEnvironment("ApiAuthentication__MetadataAddress", IdentityMetadataAddress)
    .WithEnvironment("ApiAuthentication__Audience", "evcharging-api")
    .WithEnvironment("ApiAuthentication__AllowLocalHttp", builder.ExecutionContext.IsRunMode ? "true" : "false");

gateway.WithEnvironment("BrowserAuthentication__PublicOrigin", gatewayPublicOrigin)
    .WithEnvironment("BrowserAuthentication__SignInErrorPath", "/sign-in-error")
    .WithEnvironment("BrowserAuthentication__Authority", identityAuthority)
    .WithEnvironment("BrowserAuthentication__MetadataAddress", IdentityMetadataAddress)
    .WithEnvironment("BrowserAuthentication__ClientId", "evcharging-gateway")
    .WithEnvironment("BrowserAuthentication__ClientSecret", gatewayOidcSecret)
    .WithEnvironment("BrowserAuthentication__AllowLocalHttp", builder.ExecutionContext.IsRunMode ? "true" : "false");

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
    .WithReference(databaseAccess.ChargingMessaging, "messaging")
    .WaitForCompletion(chargingMessageMigrations)
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
    .WithReference(databaseAccess.NotificationMessaging, "messaging")
    .WaitForCompletion(notificationMessageMigrations)
    .WaitForCompletion(platformMigrations)
    .WithReference(broker)
    .WaitFor(broker)
    .WithReference(notificationProviderApi)
    .WaitFor(notificationProviderApi);

foreach (var worker in new[] { charging, observation, search, cache, media })
{
    worker.WithReference(databaseAccess.Write)
        .WaitForCompletion(platformMigrations)
        .WithReference(broker)
        .WaitFor(broker);
}

foreach (var consumer in new[] { observation, search, cache })
{
    consumer.WithReference(stream).WaitFor(stream);
}

observation.WithReference(documents).WaitFor(documents);
foreach (var reader in new[] { search, cache })
{
    reader.WithReference(databaseAccess.Read);
}

cache.WithReference(cacheStore).WaitFor(cacheStore);
search.WithReference(searchIndex).WaitFor(searchIndex);

media.WithObjectStorage(storage, storageAccessKey, storageSecretKey)
    .WithReference(documents)
    .WaitFor(documents);
synchronousChargingProviderApi.WithReference(gateway).WaitFor(gateway);
queuedChargingProviderApi.WithReference(gateway).WaitFor(gateway);

builder.Build().Run();

async Task<int> InstanceCountAsync(string name)
{
    var parameter = builder.AddParameter(name, () => builder.Configuration[$"Parameters:{name}"] ?? "2");
    if (!int.TryParse(await parameter.Resource.GetValueAsync(CancellationToken.None), out var count)
        || count is < 1 or > 8)
    {
        throw new InvalidOperationException($"Parameters:{name} must be an integer between 1 and 8.");
    }

    return count;
}
