using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Persistence;

public sealed class ProviderDbContext(DbContextOptions<ProviderDbContext> options) : DbContext(options)
{
    internal DbSet<ProviderCommand> Commands => Set<ProviderCommand>();
    internal DbSet<ProviderSession> Sessions => Set<ProviderSession>();
    internal DbSet<ProviderConnector> Connectors => Set<ProviderConnector>();
    internal DbSet<ProviderObservation> Observations => Set<ProviderObservation>();
    internal DbSet<CallbackTarget> CallbackTargets => Set<CallbackTarget>();

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
        connectors.Property(connector => connector.EnergyLimitKWh).HasPrecision(10, 3);
        connectors.HasOne<ProviderCommand>().WithMany()
            .HasForeignKey(connector => connector.ActiveCommandId)
            .OnDelete(DeleteBehavior.Restrict);

        var observations = modelBuilder.Entity<ProviderObservation>();
        observations.ToTable("observations");
        observations.HasKey(observation => observation.Id);
        observations.HasIndex(observation => observation.EventId).IsUnique();
        observations.HasIndex(observation => new { observation.SessionId, observation.OccurredAt });
        observations.HasIndex(observation => new { observation.DeliveryStatus, observation.NextAttemptAt });
        observations.Property(observation => observation.State).HasConversion<string>().HasMaxLength(20);
        observations.Property(observation => observation.DeliveryStatus).HasConversion<string>().HasMaxLength(20);
        observations.Property(observation => observation.PowerKw).HasPrecision(10, 3);
        observations.Property(observation => observation.EnergyWh).HasPrecision(14, 3);
        observations.HasOne(observation => observation.Session).WithMany()
            .HasForeignKey(observation => observation.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        var callbackTargets = modelBuilder.Entity<CallbackTarget>();
        callbackTargets.ToTable("callback_targets");
        callbackTargets.HasKey(target => target.Id);
        callbackTargets.Property(target => target.Id).ValueGeneratedNever();
        callbackTargets.Property(target => target.Url).HasMaxLength(2048);
        callbackTargets.Property(target => target.ApiKey).HasMaxLength(256);
    }

    /// <summary>
    /// Locks the session row so an emission and a stop cannot interleave. The lock lasts until the open
    /// transaction ends, so the caller must have started one.
    /// </summary>
    internal async Task<ProviderSession?> LockSessionAsync(
        Guid externalSessionId,
        CancellationToken cancellationToken)
    {
        RequireTransaction();
        return (await Sessions
            .FromSql($"SELECT * FROM sessions WHERE external_session_id = {externalSessionId} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();
    }

    /// <summary>
    /// Locks active sessions so none can be stopped while a reading is written or the session is ended; sessions a
    /// stop holds are skipped.
    /// The lock lasts until the open transaction ends, so the caller must have started one.
    /// </summary>
    internal Task<List<ProviderSession>> LockActiveSessionsAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        return Sessions
            .FromSql($"SELECT * FROM sessions WHERE ended_at IS NULL FOR UPDATE SKIP LOCKED")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    // Outside a transaction a row lock is released as its own statement ends, so it would protect nothing.
    private void RequireTransaction()
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A session row lock requires an open transaction.");
        }
    }

    internal async Task SaveCallbackTargetAsync(CallbackTarget target, CancellationToken cancellationToken) =>
        await Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO callback_targets (id, url, api_key)
            VALUES ({target.Id}, {target.Url}, {target.ApiKey})
            ON CONFLICT (id) DO UPDATE SET url = EXCLUDED.url, api_key = EXCLUDED.api_key
            """,
            cancellationToken);

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

    /// <summary>Ends a session once, freeing its connector with the final observation.</summary>
    internal async Task EndSessionAsync(
        ProviderSession session, DateTimeOffset endedAt, CancellationToken cancellationToken)
    {
        if (!await TryEndSessionAsync(session.Id, endedAt, cancellationToken))
        {
            return;
        }

        await ReleaseConnectorAsync(session, cancellationToken);
        Observations.Add(ProviderObservation.Stopped(session, endedAt));
    }

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
            INSERT INTO connectors (station_id, charger_id, connector_id, power_kw, enabled, energy_limit_k_wh)
            VALUES ({connector.StationId}, {connector.ChargerId}, {connector.ConnectorId},
                {connector.PowerKw}, {connector.Enabled}, {connector.EnergyLimitKWh})
            ON CONFLICT (station_id, charger_id, connector_id)
            DO UPDATE SET power_kw = EXCLUDED.power_kw, enabled = EXCLUDED.enabled,
                energy_limit_k_wh = EXCLUDED.energy_limit_k_wh
            """,
            cancellationToken);
}
