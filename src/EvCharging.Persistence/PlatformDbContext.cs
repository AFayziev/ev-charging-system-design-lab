using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence;

public class PlatformDbContext : DbContext
{
    internal const string Schema = "charging";

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options)
    {
    }

    protected PlatformDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Charger> Chargers => Set<Charger>();
    public DbSet<Connector> Connectors => Set<Connector>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<OperatorUser> OperatorUsers => Set<OperatorUser>();
    public DbSet<OperatorAssignment> OperatorAssignments => Set<OperatorAssignment>();
    public DbSet<ChargingProvider> ChargingProviders => Set<ChargingProvider>();
    public DbSet<ProviderStation> ProviderStations => Set<ProviderStation>();
    public DbSet<ProviderCharger> ProviderChargers => Set<ProviderCharger>();
    public DbSet<ProviderConnector> ProviderConnectors => Set<ProviderConnector>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ChargingSession> Sessions => Set<ChargingSession>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<FileMetadata> Files => Set<FileMetadata>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSnakeCaseNamingConvention()
            .UseNpgsql(postgres => postgres.MigrationsHistoryTable("ef_migrations_history", Schema)
                .MigrationsAssembly("EvCharging.Persistence.Migrations"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
    }
}
