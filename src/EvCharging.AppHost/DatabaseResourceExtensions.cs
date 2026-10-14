namespace EvCharging.AppHost;

internal static class DatabaseResourceExtensions
{
    public static DatabaseConnectionResources AddDatabaseAccess(
        this IDistributedApplicationBuilder builder, IResourceBuilder<PostgresDatabaseResource> database)
    {
        var writerPassword = builder.AddPersistentSecret("platform-write-password");
        var readerPassword = builder.AddPersistentSecret("platform-read-password");
        var migratorPassword = builder.AddPersistentSecret("platform-migration-password");
        var authenticationPassword = builder.AddPersistentSecret("gateway-auth-password");
        var authenticationMigratorPassword = builder.AddPersistentSecret("gateway-auth-migration-password");
        var chargingMessagingPassword = builder.AddPersistentSecret("charging-messaging-password");
        var notificationMessagingPassword = builder.AddPersistentSecret("notification-messaging-password");

        var write = AddConnection("platform-write", "platform_writer", writerPassword);
        var read = AddConnection("platform-read", "platform_reader", readerPassword);
        var migration = AddConnection("platform-migration", "platform_migrator", migratorPassword);
        var authentication = AddConnection("gateway-auth", "gateway_auth_runtime", authenticationPassword);
        var authenticationMigration = AddConnection(
            "gateway-auth-migration-db", "gateway_auth_migrator", authenticationMigratorPassword);
        var chargingMessaging = AddConnection("charging-messaging", "charging_messaging", chargingMessagingPassword);
        var notificationMessaging = AddConnection(
            "notification-messaging", "notification_messaging", notificationMessagingPassword);

        var endpoint = database.Resource.Parent.GetEndpoint("tcp");
        var provisioning = builder.AddDockerfile("database-provisioning", "../../infra/postgres")
            .WithBuildArg("POSTGRES_IMAGE", $"docker.io/library/postgres:{ResourceExtensions.PostgresImageTag}")
            .WithEnvironment("PGHOST", endpoint.Property(EndpointProperty.Host))
            .WithEnvironment("PGPORT", endpoint.Property(EndpointProperty.Port))
            .WithEnvironment("PGDATABASE", database.Resource.DatabaseName)
            .WithEnvironment("PGUSER", "postgres")
            .WithEnvironment("PGPASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("PGCONNECT_TIMEOUT", "5")
            .WithEnvironment("PGOPTIONS", "-c statement_timeout=30000 -c lock_timeout=5000")
            .WithEnvironment("GATEWAY_AUTH_PASSWORD", authenticationPassword)
            .WithEnvironment("GATEWAY_AUTH_MIGRATION_PASSWORD", authenticationMigratorPassword)
            .WithEnvironment("PLATFORM_WRITE_PASSWORD", writerPassword)
            .WithEnvironment("PLATFORM_READ_PASSWORD", readerPassword)
            .WithEnvironment("PLATFORM_MIGRATION_PASSWORD", migratorPassword)
            .WithEnvironment("CHARGING_MESSAGING_PASSWORD", chargingMessagingPassword)
            .WithEnvironment("NOTIFICATION_MESSAGING_PASSWORD", notificationMessagingPassword)
            .WaitFor(database);

        if (builder.ExecutionContext.IsRunMode)
        {
            provisioning.WithImageTag("local");
        }

        return new DatabaseConnectionResources
        {
            Write = write,
            Read = read,
            Migration = migration,
            Authentication = authentication,
            AuthenticationMigration = authenticationMigration,
            ChargingMessaging = chargingMessaging,
            NotificationMessaging = notificationMessaging,
            Provisioning = provisioning
        };

        IResourceBuilder<ConnectionStringResource> AddConnection(
            string name, string username, IResourceBuilder<ParameterResource> password)
        {
            var databaseEndpoint = database.Resource.Parent.GetEndpoint("tcp");
            return builder.AddConnectionString(
                name,
                ReferenceExpression.Create(
                    $"Host={databaseEndpoint.Property(EndpointProperty.Host)};" +
                    $"Port={databaseEndpoint.Property(EndpointProperty.Port)};" +
                    $"Database={database.Resource.DatabaseName};Username={username};" +
                    $"Password={password};Timeout=5;Command Timeout=30"));
        }
    }

    public static IResourceBuilder<ProjectResource> AddMessageStorageMigration(
        this IDistributedApplicationBuilder builder,
        string serviceName,
        IResourceBuilder<ConnectionStringResource> migration,
        IResourceBuilder<ContainerResource> provisioning) =>
        builder.AddProject<Projects.EvCharging_Persistence_Migrations>(
            $"{serviceName}-message-migrations",
            launchProfileName: null)
            .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
            .WithEnvironment("Messaging__ServiceName", serviceName)
            .WithReference(migration, "messaging")
            .WithArgs("resources", "setup")
            .WaitForCompletion(provisioning);
}
