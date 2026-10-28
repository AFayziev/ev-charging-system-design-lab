using Microsoft.Extensions.Configuration;

namespace EvCharging.AppHost;

internal static class ResourceExtensions
{
    internal const string PostgresImageTag = "18.6-bookworm";
    internal const string SearchIndexImageTag = "9.5.4";
    internal const string KafkaImageTag = "4.3.1";
    internal const string RustFsCliImageTag = "v0.1.36";
    internal const string ObservationTopic = "observations";

    public static ReferenceExpression AddBrowserOrigin(
        this IDistributedApplicationBuilder builder,
        EndpointReference endpoint,
        string parameterName)
    {
        if (builder.ExecutionContext.IsRunMode && builder.Configuration[$"Parameters:{parameterName}"] is null)
        {
            var localEndpoint = new EndpointReference(
                endpoint.Resource,
                endpoint.EndpointName,
                KnownNetworkIdentifiers.LocalhostNetwork);
            return ReferenceExpression.Create($"{localEndpoint}");
        }

        return ReferenceExpression.Create($"{builder.AddParameter(parameterName)}");
    }

    public static IResourceBuilder<PostgresDatabaseResource> AddPlatformDatabase(
        this IDistributedApplicationBuilder builder)
    {
        var password = builder.AddPersistentSecret("postgres-password");
        var primary = builder.AddPostgres("postgres-primary", password: password)
            .WithImageRegistry("docker.io")
            .WithImage("library/postgres")
            .WithImageTag(PostgresImageTag)
            .WithDataVolume();
        if (builder.ExecutionContext.IsRunMode)
        {
            primary.WithPgAdmin(gui => gui.WithImageTag("9.15.0").WaitFor(primary));
        }

        return primary.AddDatabase("platform-direct", "evcharging");
    }

    public static IResourceBuilder<ContainerResource> AddIdentityProvider(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> gatewayOidcSecret)
    {
        var registrationMail = builder.AddMailPit("registration-mail");
        var smtp = registrationMail.GetEndpoint("smtp");
        var database = builder.AddPostgres(
            "identity-postgres",
            password: builder.AddPersistentSecret("identity-postgres-password"))
            .WithImageRegistry("docker.io")
            .WithImage("library/postgres")
            .WithImageTag(PostgresImageTag)
            .WithDataVolume()
            .AddDatabase("identity-database", "keycloak");
        var databaseEndpoint = database.Resource.Parent.GetEndpoint("tcp");
        var identity = builder.AddDockerfile("keycloak", "../../infra/keycloak/platform")
            .WithBuildArg("KEYCLOAK_IMAGE", "quay.io/keycloak/keycloak:26.8.0")
            .WithArgs("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", builder.AddPersistentSecret("keycloak-admin-password"))
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment(
                "KC_DB_URL",
                ReferenceExpression.Create(
                    $"jdbc:postgresql://{databaseEndpoint.Property(EndpointProperty.Host)}:" +
                    $"{databaseEndpoint.Property(EndpointProperty.Port)}/{database.Resource.DatabaseName}"))
            .WithEnvironment("KC_DB_USERNAME", "postgres")
            .WithEnvironment("KC_DB_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("KC_DB_POOL_INITIAL_SIZE", "5")
            .WithEnvironment("KC_DB_POOL_MIN_SIZE", "5")
            .WithEnvironment("KC_DB_POOL_MAX_SIZE", "10")
            .WithEnvironment("GATEWAY_OIDC_SECRET", gatewayOidcSecret)
            .WithEnvironment("SYNTHETIC_DRIVER_PASSWORD", builder.AddPersistentSecret("synthetic-driver-password"))
            .WithEnvironment("SYNTHETIC_OPERATOR_PASSWORD", builder.AddPersistentSecret("synthetic-operator-password"))
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithEnvironment("REGISTRATION_SMTP_HOST", smtp.Property(EndpointProperty.Host))
            .WithEnvironment("REGISTRATION_SMTP_PORT", smtp.Property(EndpointProperty.Port))
            .WithHttpEndpoint(
                port: builder.ExecutionContext.IsRunMode
                    ? builder.Configuration.GetValue("Keycloak:HttpPort", 8080) : null,
                targetPort: 8080,
                name: "http")
            .WithHttpEndpoint(targetPort: 9000, name: "management")
            .WithEndpoint("management", e => e.ExcludeReferenceEndpoint = true)
            .WithHttpHealthCheck("/health/ready", endpointName: "management")
            .WaitFor(database)
            .WaitFor(registrationMail);

        if (builder.ExecutionContext.IsRunMode)
        {
            identity.WithImageTag("local");
        }

        return identity;
    }

    public static IResourceBuilder<MongoDBDatabaseResource> AddDocumentDatabase(
        this IDistributedApplicationBuilder builder) =>
        builder.AddMongoDB("mongodb", password: builder.AddPersistentSecret("mongodb-password"))
            .WithImageRegistry("docker.io")
            .WithImage("library/mongo")
            .WithImageTag("8.3.11-noble")
            .WithDataVolume()
            .WithLocalMongoExpress()
            .AddDatabase("observations");

    public static IResourceBuilder<ValkeyResource> AddCache(this IDistributedApplicationBuilder builder) =>
        builder.AddValkey("valkey", password: builder.AddPersistentSecret("valkey-password"))
            .WithImageRegistry("docker.io")
            .WithImage("valkey/valkey")
            .WithImageTag("9.1.2")
            .WithDataVolume()
            .WithPersistence()
            .WithLocalValkeyAdmin();

    public static IResourceBuilder<RabbitMQServerResource> AddMessageBroker(
        this IDistributedApplicationBuilder builder) =>
        builder.AddRabbitMQ("rabbitmq")
            .WithLocalManagementPlugin()
            .WithImageRegistry("docker.io")
            .WithImage("library/rabbitmq")
            .WithImageTag("4.3.6-management")
            .WithDataVolume();

    public static IResourceBuilder<KafkaServerResource> AddObservationStream(
        this IDistributedApplicationBuilder builder) =>
        builder.AddKafka("kafka")
            .WithImageRegistry("docker.io")
            .WithImage("apache/kafka")
            .WithImageTag(KafkaImageTag)
            .WithDataVolume()
            // Apache Kafka needs explicit KRaft settings alongside Aspire's listener configuration.
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:29093")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("CLUSTER_ID", "MkU3OEVBNTcwNTJENDM2Qk")
            .WithLocalKafkaUI();

    // The topic definitions live in infra/kafka/topics.txt; ObservationTopic must name its entry.
    public static IResourceBuilder<ContainerResource> AddObservationStreamProvisioning(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<KafkaServerResource> stream)
    {
        var endpoint = stream.Resource.InternalEndpoint;
        var provisioning = builder.AddDockerfile("kafka-provisioning", "../../infra/kafka")
            .WithBuildArg("KAFKA_IMAGE", $"docker.io/apache/kafka:{KafkaImageTag}")
            .WithEnvironment(
                "BOOTSTRAP_SERVER",
                ReferenceExpression.Create(
                    $"{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}"))
            .WaitFor(stream);

        if (builder.ExecutionContext.IsRunMode)
        {
            provisioning.WithImageTag("local");
        }

        return provisioning;
    }

    public static IResourceBuilder<ElasticsearchResource> AddSearchIndex(this IDistributedApplicationBuilder builder) =>
        builder.AddElasticsearch("elasticsearch", password: builder.AddPersistentSecret("elasticsearch-password"))
            .WithImageRegistry("docker.elastic.co")
            .WithImage("elasticsearch/elasticsearch")
            .WithImageTag(SearchIndexImageTag)
            .WithDataVolume()
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithEnvironment("xpack.security.transport.ssl.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithLocalElasticvue();

    // The role definition lives in infra/elasticsearch/platform/search-reader-role.json.
    public static (IResourceBuilder<ConnectionStringResource> Reader, IResourceBuilder<ContainerResource> Provisioning)
        AddSearchIndexAccess(
            this IDistributedApplicationBuilder builder,
            IResourceBuilder<ElasticsearchResource> searchIndex)
    {
        var readerPassword = builder.AddPersistentSecret("search-reader-password");
        var endpoint = searchIndex.Resource.PrimaryEndpoint;
        var hostPort = ReferenceExpression.Create(
            $"{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}");
        var provisioning = builder.AddDockerfile("search-index-provisioning", "../../infra/elasticsearch/platform")
            .WithBuildArg("ELASTICSEARCH_IMAGE", $"docker.elastic.co/elasticsearch/elasticsearch:{SearchIndexImageTag}")
            .WithEnvironment("ELASTICSEARCH_URL", ReferenceExpression.Create($"http://{hostPort}"))
            .WithEnvironment("ELASTIC_PASSWORD", searchIndex.Resource.PasswordParameter)
            .WithEnvironment("SEARCH_READER_PASSWORD", readerPassword)
            .WaitFor(searchIndex);

        if (builder.ExecutionContext.IsRunMode)
        {
            provisioning.WithImageTag("local");
        }

        var reader = builder.AddConnectionString(
            "search-reader",
            ReferenceExpression.Create($"http://search_reader:{readerPassword}@{hostPort}"));
        return (reader, provisioning);
    }

    private static IResourceBuilder<MongoDBServerResource> WithLocalMongoExpress(
        this IResourceBuilder<MongoDBServerResource> database)
    {
        if (database.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            database.WithMongoExpress(gui => gui.WithImageTag("1.0.2-20-alpine3.19").WaitFor(database));
        }

        return database;
    }

    private static IResourceBuilder<RabbitMQServerResource> WithLocalManagementPlugin(
        this IResourceBuilder<RabbitMQServerResource> broker)
    {
        if (broker.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            broker.WithManagementPlugin()
                .WithEndpoint("management", endpoint => endpoint.ExcludeReferenceEndpoint = true);
        }

        return broker;
    }

    private static IResourceBuilder<KafkaServerResource> WithLocalKafkaUI(
        this IResourceBuilder<KafkaServerResource> stream)
    {
        if (stream.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            stream.WithKafkaUI(gui => gui.WithImageTag("v1.5.0")
                .WithHttpHealthCheck("/actuator/health")
                .WaitFor(stream));
        }

        return stream;
    }

    private static IResourceBuilder<ValkeyResource> WithLocalValkeyAdmin(this IResourceBuilder<ValkeyResource> cache)
    {
        var builder = cache.ApplicationBuilder;
        if (builder.ExecutionContext.IsRunMode)
        {
            builder.AddContainer("valkey-admin", "valkey/valkey-admin", "1.2.0")
                .WithImageRegistry("docker.io")
                .WithEnvironment("DEPLOYMENT_MODE", "Web")
                .WithEnvironment("VALKEY_HOST", cache.GetEndpoint("tcp").Property(EndpointProperty.Host))
                .WithEnvironment("VALKEY_PORT", cache.GetEndpoint("tcp").Property(EndpointProperty.Port))
                .WithEnvironment("VALKEY_TLS", "false")
                .WithEnvironment("VALKEY_AUTH_TYPE", "password")
                .WithEnvironment("VALKEY_USERNAME", "default")
                .WithEnvironment("VALKEY_PASSWORD", cache.Resource.PasswordParameter!)
                .WithHttpEndpoint(targetPort: 8080, name: "http")
                .WithHttpHealthCheck("/")
                .WaitFor(cache)
                .ExcludeFromManifest();
        }

        return cache;
    }

    private static IResourceBuilder<ElasticsearchResource> WithLocalElasticvue(
        this IResourceBuilder<ElasticsearchResource> search)
    {
        if (search.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            search.WithElasticvue(gui => gui.WithImageTag("1.16.0").WithHttpHealthCheck("/"));
        }

        return search;
    }

    // The root keys stay with the storage container and its provisioning; hosts get their own scoped users.
    public static (
        IResourceBuilder<ContainerResource> Resource,
        IResourceBuilder<ParameterResource> AccessKey,
        IResourceBuilder<ParameterResource> SecretKey) AddObjectStore(this IDistributedApplicationBuilder builder)
    {
        const string DataPath = "/data";
        const int ServicePort = 9000;
        const int ConsolePort = 9001;

        var accessKey = builder.AddPersistentSecret("rustfs-access-key");
        var secretKey = builder.AddPersistentSecret("rustfs-secret-key");
        var objectStore = builder.AddContainer("rustfs", "rustfs/rustfs", "1.0.1")
            .WithImageRegistry("docker.io");
        objectStore.WithVolume(VolumeNameGenerator.Generate(objectStore, "data"), DataPath)
            .WithEnvironment("RUSTFS_ACCESS_KEY", accessKey)
            .WithEnvironment("RUSTFS_SECRET_KEY", secretKey)
            .WithEnvironment("RUSTFS_VOLUMES", DataPath)
            .WithEnvironment("RUSTFS_ADDRESS", "0.0.0.0:" + ServicePort)
            .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "true")
            .WithEnvironment("RUSTFS_CONSOLE_ADDRESS", "0.0.0.0:" + ConsolePort)
            .WithHttpEndpoint(targetPort: ServicePort, name: "http")
            .WithHttpEndpoint(targetPort: ConsolePort, name: "console")
            .WithEndpoint("console", e => e.ExcludeReferenceEndpoint = true)
            .WithHttpHealthCheck("/health");
        return (objectStore, accessKey, secretKey);
    }

    // Buckets, access rules, and user policies live in infra/rustfs/platform.
    public static ObjectStoreAccess AddObjectStoreAccess(
        this IDistributedApplicationBuilder builder,
        (
            IResourceBuilder<ContainerResource> Resource,
            IResourceBuilder<ParameterResource> AccessKey,
            IResourceBuilder<ParameterResource> SecretKey) objectStore,
        ReferenceExpression webOrigin)
    {
        var endpoint = objectStore.Resource.GetEndpoint("http");
        var apiFiles = new ObjectStoreUser("api-files", builder.AddPersistentSecret("storage-api-files-secret-key"));
        var mediaWorker = new ObjectStoreUser(
            "media-worker", builder.AddPersistentSecret("storage-media-worker-secret-key"));
        var provisioning = builder.AddDockerfile("object-storage-provisioning", "../../infra/rustfs/platform")
            .WithBuildArg("RUSTFS_CLI_IMAGE", $"docker.io/rustfs/rc:{RustFsCliImageTag}")
            .WithEnvironment(
                "STORAGE_URL",
                ReferenceExpression.Create(
                    $"http://{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}"))
            .WithEnvironment("ROOT_ACCESS_KEY", objectStore.AccessKey)
            .WithEnvironment("ROOT_SECRET_KEY", objectStore.SecretKey)
            .WithEnvironment("PRIVATE_BUCKET", ObjectStoreAccess.PrivateBucket)
            .WithEnvironment("PUBLIC_BUCKET", ObjectStoreAccess.PublicBucket)
            .WithEnvironment("WEB_ORIGIN", webOrigin)
            .WithEnvironment("UPLOAD_EXPIRY_DAYS", "1")
            .WithEnvironment("API_FILES_SECRET_KEY", apiFiles.SecretKey)
            .WithEnvironment("MEDIA_WORKER_SECRET_KEY", mediaWorker.SecretKey)
            .WaitFor(objectStore.Resource);

        if (builder.ExecutionContext.IsRunMode)
        {
            provisioning.WithImageTag("local");
        }

        // Signed URLs and published media links carry this browser-facing address.
        var publicServiceUrl = builder.AddBrowserOrigin(endpoint, "object-storage-public-origin");
        return new ObjectStoreAccess(provisioning, endpoint, publicServiceUrl, apiFiles, mediaWorker);
    }

    public static IResourceBuilder<T> WithObjectStorage<T>(
        this IResourceBuilder<T> host,
        ObjectStoreAccess storageAccess,
        ObjectStoreUser user) where T : IResourceWithEnvironment, IResourceWithWaitSupport =>
        host.WithEnvironment("ObjectStorage__ServiceUrl", storageAccess.Endpoint)
            .WithEnvironment("ObjectStorage__PublicServiceUrl", storageAccess.PublicServiceUrl)
            .WithEnvironment("ObjectStorage__AccessKey", user.AccessKey)
            .WithEnvironment("ObjectStorage__SecretKey", user.SecretKey)
            .WithEnvironment("ObjectStorage__PrivateBucket", ObjectStoreAccess.PrivateBucket)
            .WithEnvironment("ObjectStorage__PublicBucket", ObjectStoreAccess.PublicBucket)
            .WithEnvironment(
                "ObjectStorage__PublicBaseUrl",
                ReferenceExpression.Create($"{storageAccess.PublicServiceUrl}/{ObjectStoreAccess.PublicBucket}"))
            .WaitForCompletion(storageAccess.Provisioning);

    internal static IResourceBuilder<ParameterResource> AddPersistentSecret(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(
            name,
            new GenerateParameterDefault { MinLength = 32, Special = false },
            secret: true,
            persist: true);
}
