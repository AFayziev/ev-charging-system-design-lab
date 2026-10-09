using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    private const string Schema = "gateway_auth";

    public DbSet<AuthenticationSession> Sessions => Set<AuthenticationSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSnakeCaseNamingConvention()
            .UseNpgsql(postgres => postgres.MigrationsHistoryTable("ef_migrations_history", Schema)
                .MigrationsAssembly("EvCharging.Gateway.Migrations"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuthenticationSession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.HasIndex(session => session.ExpiresAt);
        });
    }
}
