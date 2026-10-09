using Microsoft.Extensions.Configuration;

namespace EvCharging.AppHost;

internal static class ResourceExtensions
{
    internal const string PostgresImageTag = "17.11-bookworm";

    public static ReferenceExpression AddBrowserOrigin(this IDistributedApplicationBuilder builder,
        EndpointReference endpoint, string parameterName) =>
        builder.ExecutionContext.IsRunMode && builder.Configuration[$"Parameters:{parameterName}"] is null
            ? ReferenceExpression.Create($"{new EndpointReference(endpoint.Resource, endpoint.EndpointName, KnownNetworkIdentifiers.LocalhostNetwork)}")
            : ReferenceExpression.Create($"{builder.AddParameter(parameterName)}");

    public static (
        IResourceBuilder<PostgresDatabaseResource> Direct,
        IResourceBuilder<ConnectionStringResource> Write,
        IResourceBuilder<ConnectionStringResource> Read) AddPlatformDatabase(this IDistributedApplicationBuilder builder)
    {
        var password = builder.AddPersistentSecret("postgres-password");
        var primary = builder.AddPostgres("postgres-primary", password: password)
            .WithImageRegistry("docker.io").WithImage("library/postgres").WithImageTag(PostgresImageTag)
            .WithDataVolume();
        if (builder.ExecutionContext.IsRunMode)
        {
            primary.WithPgAdmin(gui => gui.WithImageTag("9.15.0").WaitFor(primary));
        }

        var direct = primary.AddDatabase("platform-direct", "evcharging");
        var write = builder.AddConnectionString("platform-write", direct.Resource.ConnectionStringExpression);
        var read = builder.AddConnectionString("platform-read", direct.Resource.ConnectionStringExpression);
        return (direct, write, read);
    }

    public static IResourceBuilder<ContainerResource> AddIdentityProvider(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> gatewayOidcSecret)
    {
        var database = builder.AddPostgres("identity-postgres", password: builder.AddPersistentSecret("identity-postgres-password"))
            .WithImageRegistry("docker.io").WithImage("library/postgres").WithImageTag(PostgresImageTag)
            .WithDataVolume().AddDatabase("identity-database", "keycloak");
        var identity = builder.AddDockerfile("keycloak", "../../infra/keycloak")
            .WithBuildArg("KEYCLOAK_IMAGE", "quay.io/keycloak/keycloak:26.8.0")
            .WithArgs("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", builder.AddPersistentSecret("keycloak-admin-password"))
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", ReferenceExpression.Create($"jdbc:postgresql://{database.Resource.Parent.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{database.Resource.Parent.GetEndpoint("tcp").Property(EndpointProperty.Port)}/{database.Resource.DatabaseName}"))
            .WithEnvironment("KC_DB_USERNAME", "postgres")
            .WithEnvironment("KC_DB_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("KC_DB_POOL_INITIAL_SIZE", "5")
            .WithEnvironment("KC_DB_POOL_MIN_SIZE", "5")
            .WithEnvironment("KC_DB_POOL_MAX_SIZE", "10")
            .WithEnvironment("GATEWAY_OIDC_SECRET", gatewayOidcSecret)
            .WithEnvironment("SYNTHETIC_DRIVER_PASSWORD", builder.AddPersistentSecret("synthetic-driver-password"))
            .WithEnvironment("SYNTHETIC_OPERATOR_PASSWORD", builder.AddPersistentSecret("synthetic-operator-password"))
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode
                ? builder.Configuration.GetValue("Keycloak:HttpPort", 8080) : null, targetPort: 8080, name: "http")
            .WithHttpEndpoint(targetPort: 9000, name: "management")
            .WithEndpoint("management", e => e.ExcludeReferenceEndpoint = true)
            .WithHttpHealthCheck("/health/ready", endpointName: "management")
            .WaitFor(database);

        if (builder.ExecutionContext.IsRunMode)
        {
            identity.WithImageTag("local");
        }

        return identity;
    }

    public static IResourceBuilder<MongoDBDatabaseResource> AddDocumentDatabase(this IDistributedApplicationBuilder builder) =>
        builder.AddMongoDB("mongodb", password: builder.AddPersistentSecret("mongodb-password"))
            .WithImageRegistry("docker.io").WithImage("library/mongo").WithImageTag("8.3.11-noble")
            .WithDataVolume()
            .WithLocalMongoExpress()
            .AddDatabase("observations");

    public static IResourceBuilder<ValkeyResource> AddCache(this IDistributedApplicationBuilder builder) =>
        builder.AddValkey("valkey", password: builder.AddPersistentSecret("valkey-password"))
            .WithImageRegistry("docker.io").WithImage("valkey/valkey").WithImageTag("9.1.2")
            .WithDataVolume().WithPersistence().WithLocalValkeyAdmin();

    public static IResourceBuilder<RabbitMQServerResource> AddMessageBroker(this IDistributedApplicationBuilder builder) =>
        builder.AddRabbitMQ("rabbitmq")
            .WithLocalManagementPlugin()
            .WithImageRegistry("docker.io").WithImage("library/rabbitmq").WithImageTag("4.3.6-management")
            .WithDataVolume();

    public static IResourceBuilder<KafkaServerResource> AddObservationStream(this IDistributedApplicationBuilder builder) =>
        builder.AddKafka("kafka")
            .WithImageRegistry("docker.io").WithImage("apache/kafka").WithImageTag("4.3.1")
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

    public static IResourceBuilder<ElasticsearchResource> AddSearchIndex(this IDistributedApplicationBuilder builder) =>
        builder.AddElasticsearch("elasticsearch", password: builder.AddPersistentSecret("elasticsearch-password"))
            .WithImageRegistry("docker.elastic.co").WithImage("elasticsearch/elasticsearch").WithImageTag("9.5.4")
            .WithDataVolume()
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithEnvironment("xpack.security.transport.ssl.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithLocalElasticvue();

    private static IResourceBuilder<MongoDBServerResource> WithLocalMongoExpress(this IResourceBuilder<MongoDBServerResource> database)
    {
        if (database.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            database.WithMongoExpress(gui => gui.WithImageTag("1.0.2-20-alpine3.19").WaitFor(database));
        }

        return database;
    }

    private static IResourceBuilder<RabbitMQServerResource> WithLocalManagementPlugin(this IResourceBuilder<RabbitMQServerResource> broker)
    {
        if (broker.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            broker.WithManagementPlugin()
                .WithEndpoint("management", endpoint => endpoint.ExcludeReferenceEndpoint = true);
        }

        return broker;
    }

    private static IResourceBuilder<KafkaServerResource> WithLocalKafkaUI(this IResourceBuilder<KafkaServerResource> stream)
    {
        if (stream.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            stream.WithKafkaUI(gui => gui.WithImageTag("v1.5.0")
                .WithHttpHealthCheck("/actuator/health").WaitFor(stream));
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

    private static IResourceBuilder<ElasticsearchResource> WithLocalElasticvue(this IResourceBuilder<ElasticsearchResource> search)
    {
        if (search.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            search.WithElasticvue(gui => gui.WithImageTag("1.16.0").WithHttpHealthCheck("/"));
        }

        return search;
    }

    public static (
        IResourceBuilder<ContainerResource> Resource,
        IResourceBuilder<ParameterResource> AccessKey,
        IResourceBuilder<ParameterResource> SecretKey) AddObjectStorage(this IDistributedApplicationBuilder builder)
    {
        const string DataPath = "/data";
        const int ServicePort = 9000;
        const int ConsolePort = 9001;

        var accessKey = builder.AddPersistentSecret("rustfs-access-key");
        var secretKey = builder.AddPersistentSecret("rustfs-secret-key");
        var storage = builder.AddContainer("rustfs", "rustfs/rustfs", "1.0.1")
            .WithImageRegistry("docker.io");
        storage.WithVolume(VolumeNameGenerator.Generate(storage, "data"), DataPath)
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
        return (storage, accessKey, secretKey);
    }

    public static IResourceBuilder<T> WithObjectStorage<T>(this IResourceBuilder<T> host,
        IResourceBuilder<ContainerResource> storage,
        IResourceBuilder<ParameterResource> accessKey,
        IResourceBuilder<ParameterResource> secretKey) where T : IResourceWithEnvironment, IResourceWithWaitSupport =>
        host.WithReference(storage.GetEndpoint("http")).WaitFor(storage)
            .WithEnvironment("Storage__AccessKey", accessKey).WithEnvironment("Storage__SecretKey", secretKey);

    internal static IResourceBuilder<ParameterResource> AddPersistentSecret(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(name, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
}
