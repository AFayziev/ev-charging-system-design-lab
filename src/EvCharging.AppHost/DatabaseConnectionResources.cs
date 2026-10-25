namespace EvCharging.AppHost;

internal sealed class DatabaseConnectionResources
{
    /// <summary>Runtime connection for charging-data writes and API message storage.</summary>
    public required IResourceBuilder<ConnectionStringResource> Write { get; init; }

    /// <summary>Read-only runtime connection for charging data.</summary>
    public required IResourceBuilder<ConnectionStringResource> Read { get; init; }

    /// <summary>Schema-owning connection for charging migrations and message-store provisioning.</summary>
    public required IResourceBuilder<ConnectionStringResource> Migration { get; init; }

    /// <summary>Runtime connection for gateway authentication data.</summary>
    public required IResourceBuilder<ConnectionStringResource> Authentication { get; init; }

    /// <summary>Schema-owning connection for gateway authentication migrations.</summary>
    public required IResourceBuilder<ConnectionStringResource> AuthenticationMigration { get; init; }

    /// <summary>Runtime connection for ChargingWorker message storage and reservation processing.</summary>
    public required IResourceBuilder<ConnectionStringResource> ChargingMessaging { get; init; }

    /// <summary>Runtime connection for NotificationWorker message storage and notification preparation.</summary>
    public required IResourceBuilder<ConnectionStringResource> NotificationMessaging { get; init; }

    /// <summary>Runtime connection for SearchIndexWorker message storage and station reads.</summary>
    public required IResourceBuilder<ConnectionStringResource> SearchMessaging { get; init; }

    /// <summary>One-shot job that provisions database roles, schemas, and grants before migrations.</summary>
    public required IResourceBuilder<ContainerResource> Provisioning { get; init; }
}
