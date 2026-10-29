using System.ComponentModel.DataAnnotations;
using Aspire.Hosting.JavaScript;
using EvCharging.AppHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var chargingProviders = BindValidatedConfigSection<ChargingProviderOptions>(ChargingProviderOptions.SectionName);
var chargingSettings = BindValidatedConfigSection<ChargingOptions>(ChargingOptions.SectionName);
var searchSettings = BindValidatedConfigSection<SearchOptions>(SearchOptions.SectionName);
var mediaSettings = BindValidatedConfigSection<StationMediaOptions>(StationMediaOptions.SectionName);

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
var searchMessageMigrations = builder.AddMessageStorageMigration(
    "search", databaseAccess.Migration, databaseAccess.Provisioning);
var mediaMessageMigrations = builder.AddMessageStorageMigration(
    "media", databaseAccess.Migration, databaseAccess.Provisioning);

var gatewayOidcSecret = builder.AddPersistentSecret("gateway-oidc-secret");
var wattHarborCallbackKey = builder.AddPersistentSecret("wattharbor-callback-key");
var identity = builder.AddIdentityProvider(gatewayOidcSecret);

var documents = builder.AddDocumentDatabase();
var cacheStore = builder.AddCache();
var broker = builder.AddMessageBroker();
var stream = builder.AddObservationStream();
var streamProvisioning = builder.AddObservationStreamProvisioning(stream);
var searchIndex = builder.AddSearchIndex();
var (searchReader, searchIndexProvisioning) = builder.AddSearchIndexAccess(searchIndex);
var objectStore = builder.AddObjectStore();

var apiCount = await InstanceCountAsync("api-instances");
var gatewayCount = await InstanceCountAsync("gateway-instances");
var api = builder.AddProject<Projects.EvCharging_Api>("api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithSettings(
        chargingSettings,
        ChargingOptions.SectionName,
        options => options.StatusCheckTimeout,
        options => options.FreshnessLimit,
        options => options.SilenceLimit,
        options => options.ClockSkew)
    .WithHttpEndpoint()
    .WithReplicas(apiCount)
    .WithHttpHealthCheck("/health");

api.WithSettings(
    mediaSettings,
    StationMediaOptions.SectionName,
    options => options.MaximumPhotosPerStation,
    options => options.MaximumPhotoBytes,
    options => options.MaximumPhotoPixels,
    options => options.PhotoContentTypes,
    options => options.UploadUrlLifetime)
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
    .WaitFor(stream)
    .WaitForCompletion(streamProvisioning)
    .WithEnvironment("ObservationStream__Topic", EvCharging.AppHost.ResourceExtensions.ObservationTopic)
    .WithEnvironment(
        $"ObservationSources__Sources__{chargingProviders.WattHarborIntegrationKey}__Client", "WattHarbor")
    .WithEnvironment(
        $"ObservationSources__Sources__{chargingProviders.WattHarborIntegrationKey}__ApiKeys__0",
        wattHarborCallbackKey);

var searchApiCount = await InstanceCountAsync("search-api-instances");
var searchApi = builder.AddProject<Projects.EvCharging_SearchApi>("search-api", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithSettings(
        searchSettings,
        SearchOptions.SectionName,
        options => options.RequestTimeout,
        options => options.RetryAttempts,
        options => options.RetryDelay)
    .WithHttpEndpoint()
    .WithReplicas(searchApiCount)
    .WithHttpHealthCheck("/health")
    .WithReference(searchReader)
    .WaitForCompletion(searchIndexProvisioning);

var gateway = builder.AddProject<Projects.EvCharging_Gateway>("gateway", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode
        ? builder.Configuration.GetValue("Gateway:HttpPort", 5100) : null)
    .WithReplicas(gatewayCount)
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api)
    .WithReference(searchApi)
    .WithSettings(searchSettings, SearchOptions.SectionName, options => options.GatewayTimeout);

if (builder.Environment.IsDevelopment())
{
    foreach (var httpApi in new[] { api, searchApi, gateway })
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

var frontendPreview = builder.ExecutionContext.IsRunMode && builder.Configuration["Frontend:Mode"] == "preview";
var frontend = builder.AddViteApp("frontend", "../EvCharging.Web", frontendPreview ? "preview" : "dev")
    .WithEndpoint(
        "http",
        endpoint => endpoint.Port = builder.ExecutionContext.IsRunMode
            ? builder.Configuration.GetValue("Frontend:HttpPort", 5173) : null)
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

if (frontendPreview)
{
    var frontendBuild = builder.AddExecutable("frontend-build", "npm", "../EvCharging.Web", "run", "build");
    if (frontend.Resource.TryGetLastAnnotation<JavaScriptPackageInstallerAnnotation>(out var installer))
    {
        frontendBuild.WaitForCompletion(builder.CreateResourceBuilder(installer.Resource));
    }

    var sentryOrg = builder.Configuration["Frontend:Sentry:Org"];
    var sentryProject = builder.Configuration["Frontend:Sentry:Project"];
    if (!string.IsNullOrWhiteSpace(sentryOrg) && !string.IsNullOrWhiteSpace(sentryProject)
        && !string.IsNullOrWhiteSpace(builder.Configuration["Parameters:sentry-auth-token"]))
    {
        frontendBuild
            .WithEnvironment("SENTRY_UPLOAD", "true")
            .WithEnvironment("SENTRY_ORG", sentryOrg)
            .WithEnvironment("SENTRY_PROJECT", sentryProject)
            .WithEnvironment("SENTRY_AUTH_TOKEN", builder.AddParameter("sentry-auth-token", secret: true))
            .WithEnvironment("SENTRY_RELEASE", builder.Configuration["FrontendDiagnostics:Release"] ?? string.Empty);
    }

    frontend.WaitForCompletion(frontendBuild);
}

var gatewayPublicOrigin = builder.AddBrowserOrigin(frontend.GetEndpoint("http"), "gateway-public-origin");
var identityPublicOrigin = builder.AddBrowserOrigin(identity.GetEndpoint("http"), "identity-public-origin");
identity.WithEnvironment("GATEWAY_PUBLIC_ORIGIN", gatewayPublicOrigin)
    .WithEnvironment("KC_HOSTNAME", identityPublicOrigin)
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");

var identityAuthority = ReferenceExpression.Create($"{identityPublicOrigin}/realms/evcharging");
const string IdentityMetadataAddress = "http://keycloak/realms/evcharging/.well-known/openid-configuration";

platformMigrations.WithEnvironment("LabData__Enabled", "true")
    .WithEnvironment("LabData__IdentityIssuer", identityAuthority)
    .WithEnvironment("LabData__WattHarborIntegrationKey", chargingProviders.WattHarborIntegrationKey);

if (builder.ExecutionContext.IsRunMode)
{
    builder.AddProject<Projects.EvCharging_Persistence_Migrations>("lab-data-reset", launchProfileName: null)
        .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
        .WithEnvironment("LabData__Enabled", "true")
        .WithEnvironment("LabData__Reset", "true")
        .WithEnvironment("LabData__IdentityIssuer", identityAuthority)
        .WithEnvironment("LabData__WattHarborIntegrationKey", chargingProviders.WattHarborIntegrationKey)
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

var wattHarborApi = builder.AddExternalService(
    "wattharbor-provider-api", builder.AddParameter("wattharbor-provider-url"));
var ampTrailApi = builder.AddExternalService(
    "amptrail-provider-api", builder.AddParameter("amptrail-provider-url"));
var notificationViaSes = string.Equals(
    builder.Configuration["NotificationDelivery:Provider"], "AmazonSes", StringComparison.OrdinalIgnoreCase);
var wattHarborTokenEndpoint = builder.AddParameter("wattharbor-provider-token-endpoint");
var wattHarborClientSecret = builder.AddParameter("wattharbor-provider-client-secret", secret: true);
string wattHarborIntegrationPrefix
    = $"ChargingProviders__Integrations__{chargingProviders.WattHarborIntegrationKey}__";

var charging = builder.AddProject<Projects.EvCharging_ChargingWorker>("charging-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment(
        $"{wattHarborIntegrationPrefix}TokenEndpoint", wattHarborTokenEndpoint)
    .WithEnvironment(
        $"{wattHarborIntegrationPrefix}ClientSecret", wattHarborClientSecret)
    .WithEnvironment(
        $"{wattHarborIntegrationPrefix}AllowLocalHttp", builder.ExecutionContext.IsRunMode ? "true" : "false")
    .WithEnvironment($"{wattHarborIntegrationPrefix}Client", "WattHarbor")
    .WithEnvironment(
        $"{wattHarborIntegrationPrefix}BaseAddress",
        $"https+http://{wattHarborApi.Resource.Name}/")
    .WithEnvironment($"{wattHarborIntegrationPrefix}ClientId", "charging-worker")
    .WithEnvironment($"{wattHarborIntegrationPrefix}RequestTimeout", "00:00:03")
    .WithSettings(
        chargingSettings,
        ChargingOptions.SectionName,
        options => options.FreshnessLimit,
        options => options.ClockSkew)
    .WithEnvironment("ObservationStream__Topic", EvCharging.AppHost.ResourceExtensions.ObservationTopic)
    .WithReference(databaseAccess.ChargingMessaging, "messaging")
    .WaitForCompletion(chargingMessageMigrations)
    .WithReference(gateway)
    .WithReference(wattHarborApi)
    .WithReference(ampTrailApi)
    .WaitFor(gateway);

var observation = builder.AddProject<Projects.EvCharging_ObservationWorker>("observation-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithSettings(chargingSettings, ChargingOptions.SectionName, options => options.ClockSkew);

var search = builder.AddProject<Projects.EvCharging_SearchIndexWorker>("search-index-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(databaseAccess.SearchMessaging, "messaging")
    .WaitForCompletion(searchMessageMigrations)
    .WaitForCompletion(platformMigrations)
    .WithReference(broker)
    .WaitFor(broker);

var cache = builder.AddProject<Projects.EvCharging_CacheWorker>("cache-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);

var media = builder.AddProject<Projects.EvCharging_MediaWorker>("media-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithSettings(mediaSettings, StationMediaOptions.SectionName, options => options.MaximumPhotoPixels)
    .WithReference(databaseAccess.MediaMessaging, "messaging")
    .WaitForCompletion(mediaMessageMigrations)
    .WaitForCompletion(platformMigrations)
    .WithReference(broker)
    .WaitFor(broker);

var notificationCount = await InstanceCountAsync("notification-worker-instances");
var notification = builder.AddProject<Projects.EvCharging_NotificationWorker>("notification-worker")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReplicas(notificationCount)
    .WithReference(databaseAccess.NotificationMessaging, "messaging")
    .WaitForCompletion(notificationMessageMigrations)
    .WaitForCompletion(platformMigrations)
    .WithReference(broker)
    .WaitFor(broker);

var notificationSenderName = builder.Configuration["NotificationDelivery:SenderName"];
if (!string.IsNullOrWhiteSpace(notificationSenderName))
{
    notification.WithEnvironment("NotificationDelivery__SenderName", notificationSenderName);
}

if (notificationViaSes)
{
    notification
        .WithEnvironment("NotificationDelivery__Provider", "AmazonSes")
        .WithEnvironment("NotificationDelivery__SenderEmail", builder.AddParameter("ses-sender-email"))
        .WithEnvironment("AWS_REGION", builder.AddParameter("ses-region"))
        .WithEnvironment("AWS_ACCESS_KEY_ID", builder.AddParameter("ses-access-key-id", secret: true))
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", builder.AddParameter("ses-secret-access-key", secret: true));
}
else
{
    var notificationMail = builder.AddMailPit("notification-mail").WithDataVolume("notification-mail-data");
    notification.WithReference(notificationMail.GetEndpoint("http")).WaitFor(notificationMail);
}

foreach (var worker in new[] { charging, observation, cache })
{
    worker.WithReference(databaseAccess.Write)
        .WaitForCompletion(platformMigrations)
        .WithReference(broker)
        .WaitFor(broker);
}

foreach (var consumer in new[] { charging, observation, search, cache })
{
    consumer.WithReference(stream).WaitFor(stream).WaitForCompletion(streamProvisioning);
}

var documentMigrations = builder.AddProject<Projects.EvCharging_DocumentStore_Migrations>(
    "document-store-migrations",
    launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(documents)
    .WaitFor(documents);

observation.WithReference(documents)
    .WaitFor(documents)
    .WaitForCompletion(documentMigrations)
    .WithEnvironment("ObservationStream__Topic", EvCharging.AppHost.ResourceExtensions.ObservationTopic);
cache.WithReference(databaseAccess.Read);

cache.WithReference(cacheStore).WaitFor(cacheStore);
search.WithReference(searchIndex).WaitFor(searchIndex);

var objectStorage = builder.AddObjectStoreAccess(objectStore, gatewayPublicOrigin);
api.WithObjectStorage(objectStorage, objectStorage.ApiFiles);
media.WithObjectStorage(objectStorage, objectStorage.MediaWorker);

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

T BindValidatedConfigSection<T>(string section)
    where T : new()
{
    var options = new T();
    builder.Configuration.GetRequiredSection(section).Bind(options);
    Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
    return options;
}
