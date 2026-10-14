using EvCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    internal const string IdentityIndexName = "ix_drivers_identity_issuer_identity_subject";

    public void Configure(EntityTypeBuilder<Driver> entity)
    {
        entity.Property(driver => driver.Email).HasMaxLength(EmailAddress.MaximumLength).IsRequired();
        entity.HasIndex(driver => new { driver.IdentityIssuer, driver.IdentitySubject })
            .IsUnique()
            .HasDatabaseName(IdentityIndexName);
    }
}
