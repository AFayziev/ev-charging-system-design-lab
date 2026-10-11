using EvCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> entity)
    {
        entity.HasIndex(driver => new { driver.IdentityIssuer, driver.IdentitySubject }).IsUnique();
    }
}
