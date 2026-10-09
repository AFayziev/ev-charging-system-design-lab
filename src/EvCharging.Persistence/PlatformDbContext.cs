using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    private const string Schema = "charging";

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Charger> Chargers => Set<Charger>();
    public DbSet<Evse> Evses => Set<Evse>();
    public DbSet<Connector> Connectors => Set<Connector>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<OperatorUser> OperatorUsers => Set<OperatorUser>();
    public DbSet<OperatorAssignment> OperatorAssignments => Set<OperatorAssignment>();
    public DbSet<ChargingProvider> ChargingProviders => Set<ChargingProvider>();
    public DbSet<ProviderStation> ProviderStations => Set<ProviderStation>();
    public DbSet<ProviderCharger> ProviderChargers => Set<ProviderCharger>();
    public DbSet<ProviderEvse> ProviderEvses => Set<ProviderEvse>();
    public DbSet<ProviderConnector> ProviderConnectors => Set<ProviderConnector>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ChargingSession> Sessions => Set<ChargingSession>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Deadline> Deadlines => Set<Deadline>();
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
