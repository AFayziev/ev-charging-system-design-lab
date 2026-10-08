using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AuthenticationSession> Sessions => Set<AuthenticationSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("gateway_auth");
        modelBuilder.Entity<AuthenticationSession>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasMaxLength(128);
            entity.Property(session => session.Version).IsConcurrencyToken();
            entity.HasIndex(session => session.ExpiresAt);
        });
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
