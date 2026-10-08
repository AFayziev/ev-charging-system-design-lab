namespace EvCharging.AppHost;

internal static class AuthenticationResourceExtensions
{
    public static (
        IResourceBuilder<ConnectionStringResource> Runtime,
        IResourceBuilder<ConnectionStringResource> Migration,
        IResourceBuilder<ContainerResource> Bootstrap) AddAuthenticationDatabase(
        this IDistributedApplicationBuilder builder, IResourceBuilder<PostgresDatabaseResource> database)
    {
        var runtimePassword = builder.AddPersistentSecret("gateway-auth-password");
        var migrationPassword = builder.AddPersistentSecret("gateway-auth-migration-password");
        var endpoint = database.Resource.Parent.GetEndpoint("tcp");
        var runtime = builder.AddConnectionString("gateway-auth", ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database={database.Resource.DatabaseName};Username=gateway_auth_runtime;Password={runtimePassword}"));
        var migration = builder.AddConnectionString("gateway-auth-migration-db", ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database={database.Resource.DatabaseName};Username=gateway_auth_migrator;Password={migrationPassword};Timeout=5;Command Timeout=30"));
        var bootstrap = builder.AddDockerfile("gateway-auth-bootstrap", "../../infra/postgres")
            .WithBuildArg("POSTGRES_IMAGE", $"docker.io/library/postgres:{ResourceExtensions.PostgresImageTag}")
            .WithEnvironment("PGHOST", endpoint.Property(EndpointProperty.Host))
            .WithEnvironment("PGPORT", endpoint.Property(EndpointProperty.Port))
            .WithEnvironment("PGDATABASE", database.Resource.DatabaseName)
            .WithEnvironment("PGUSER", "postgres")
            .WithEnvironment("PGPASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("PGCONNECT_TIMEOUT", "5")
            .WithEnvironment("PGOPTIONS", "-c statement_timeout=30000 -c lock_timeout=5000")
            .WithEnvironment("GATEWAY_AUTH_PASSWORD", runtimePassword)
            .WithEnvironment("GATEWAY_AUTH_MIGRATION_PASSWORD", migrationPassword)
            .WaitFor(database);
        return (runtime, migration, bootstrap);
    }
}
