namespace EvCharging.AppHost;

internal static class DatabaseResourceExtensions
{
    public static (
        IResourceBuilder<ConnectionStringResource> Write,
        IResourceBuilder<ConnectionStringResource> Read,
        IResourceBuilder<ConnectionStringResource> Migration,
        IResourceBuilder<ConnectionStringResource> Authentication,
        IResourceBuilder<ConnectionStringResource> AuthenticationMigration,
        IResourceBuilder<ContainerResource> Bootstrap) AddDatabaseAccess(
            this IDistributedApplicationBuilder builder, IResourceBuilder<PostgresDatabaseResource> database)
    {
        var writerPassword = builder.AddPersistentSecret("platform-write-password");
        var readerPassword = builder.AddPersistentSecret("platform-read-password");
        var migratorPassword = builder.AddPersistentSecret("platform-migration-password");
        var authenticationPassword = builder.AddPersistentSecret("gateway-auth-password");
        var authenticationMigratorPassword = builder.AddPersistentSecret("gateway-auth-migration-password");

        var write = AddConnection("platform-write", "platform_writer", writerPassword);
        var read = AddConnection("platform-read", "platform_reader", readerPassword);
        var migration = AddConnection("platform-migration", "platform_migrator", migratorPassword);
        var authentication = AddConnection("gateway-auth", "gateway_auth_runtime", authenticationPassword);
        var authenticationMigration = AddConnection(
            "gateway-auth-migration-db", "gateway_auth_migrator", authenticationMigratorPassword);

        var endpoint = database.Resource.Parent.GetEndpoint("tcp");
        var bootstrap = builder.AddDockerfile("database-provisioning", "../../infra/postgres")
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
            .WaitFor(database);

        if (builder.ExecutionContext.IsRunMode)
        {
            bootstrap.WithImageTag("local");
        }

        return (write, read, migration, authentication, authenticationMigration, bootstrap);

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
}
