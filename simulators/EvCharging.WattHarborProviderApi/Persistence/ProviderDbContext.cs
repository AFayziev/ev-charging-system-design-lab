using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Persistence;

public sealed class ProviderDbContext(DbContextOptions<ProviderDbContext> options) : DbContext(options)
{
    internal DbSet<ProviderCommand> Commands => Set<ProviderCommand>();
    internal DbSet<ProviderSession> Sessions => Set<ProviderSession>();
    internal DbSet<ProviderConnector> Connectors => Set<ProviderConnector>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var commands = modelBuilder.Entity<ProviderCommand>();
        commands.ToTable("commands");
        commands.HasKey(command => command.Id);
        commands.HasIndex(command => command.PublicId).IsUnique();
        commands.HasIndex(command => command.ExternalOperationId).IsUnique();
        commands.Property(command => command.StationId).HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        commands.Property(command => command.ChargerId).HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        commands.Property(command => command.ConnectorId)
            .HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        commands.Property(command => command.RejectionCode).HasMaxLength(100);
        commands.HasOne(command => command.Session).WithMany()
            .HasForeignKey(command => command.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        var sessions = modelBuilder.Entity<ProviderSession>();
        sessions.ToTable("sessions");
        sessions.HasKey(session => session.Id);
        sessions.HasIndex(session => session.PublicId).IsUnique();
        sessions.HasIndex(session => session.ExternalSessionId).IsUnique();
        sessions.Property(session => session.StationId).HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        sessions.Property(session => session.ChargerId).HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        sessions.Property(session => session.ConnectorId)
            .HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        sessions.Property(session => session.PowerKw).HasPrecision(10, 3);

        var connectors = modelBuilder.Entity<ProviderConnector>();
        connectors.ToTable("connectors");
        connectors.HasKey(connector => new { connector.StationId, connector.ChargerId, connector.ConnectorId });
        connectors.Property(connector => connector.StationId)
            .HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        connectors.Property(connector => connector.ChargerId)
            .HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        connectors.Property(connector => connector.ConnectorId)
            .HasMaxLength(ProviderContractLimits.EquipmentIdentifierLength);
        connectors.Property(connector => connector.PowerKw).HasPrecision(10, 3);
        connectors.HasOne<ProviderCommand>().WithMany()
            .HasForeignKey(connector => connector.ActiveCommandId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    internal async Task<bool> TryClaimCommandAsync(ProviderCommand command, CancellationToken cancellationToken) =>
        await Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO commands
                (id, public_id, kind, external_operation_id, station_id, charger_id, connector_id, external_session_id,
                    created_at)
            VALUES ({command.Id}, {command.PublicId}, {command.Kind}, {command.ExternalOperationId},
                {command.StationId}, {command.ChargerId}, {command.ConnectorId}, {command.ExternalSessionId},
                {command.CreatedAt})
            ON CONFLICT (external_operation_id) DO NOTHING
            """,
            cancellationToken) == 1;

    internal async Task<bool> TryClaimConnectorAsync(ProviderCommand command, CancellationToken cancellationToken) =>
        await Connectors
            .Where(connector => connector.StationId == command.StationId
                && connector.ChargerId == command.ChargerId
                && connector.ConnectorId == command.ConnectorId
                && connector.Enabled
                && connector.ActiveCommandId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(connector => connector.ActiveCommandId, command.Id),
                cancellationToken) == 1;

    internal async Task<bool> TryCreateSessionAsync(ProviderSession session, CancellationToken cancellationToken) =>
        await Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sessions
                (id, public_id, external_session_id, station_id, charger_id, connector_id, started_at, power_kw)
            VALUES ({session.Id}, {session.PublicId}, {session.ExternalSessionId}, {session.StationId},
                {session.ChargerId}, {session.ConnectorId}, {session.StartedAt}, {session.PowerKw})
            ON CONFLICT (external_session_id) DO NOTHING
            """,
            cancellationToken) == 1;

    /// <summary>Ends a session once; false means another stop already ended it.</summary>
    internal async Task<bool> TryEndSessionAsync(
        Guid sessionId, DateTimeOffset endedAt, CancellationToken cancellationToken) =>
        await Sessions
            .Where(session => session.Id == sessionId && session.EndedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.EndedAt, endedAt),
                cancellationToken) == 1;

    internal Task ReleaseConnectorAsync(ProviderSession session, CancellationToken cancellationToken) =>
        Connectors
            .Where(connector => connector.StationId == session.StationId
                && connector.ChargerId == session.ChargerId
                && connector.ConnectorId == session.ConnectorId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(connector => connector.ActiveCommandId, (Guid?)null),
                cancellationToken);

    internal Task ReleaseConnectorAsync(Guid commandId, CancellationToken cancellationToken) =>
        Connectors
            .Where(connector => connector.ActiveCommandId == commandId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(connector => connector.ActiveCommandId, (Guid?)null),
                cancellationToken);

    internal Task ConfigureConnectorAsync(ProviderConnector connector, CancellationToken cancellationToken) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO connectors (station_id, charger_id, connector_id, power_kw, enabled)
            VALUES ({connector.StationId}, {connector.ChargerId}, {connector.ConnectorId},
                {connector.PowerKw}, {connector.Enabled})
            ON CONFLICT (station_id, charger_id, connector_id)
            DO UPDATE SET power_kw = EXCLUDED.power_kw, enabled = EXCLUDED.enabled
            """,
            cancellationToken);
}
