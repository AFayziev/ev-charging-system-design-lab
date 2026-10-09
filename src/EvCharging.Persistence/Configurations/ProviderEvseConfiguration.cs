using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderEvseConfiguration : IEntityTypeConfiguration<ProviderEvse>
{
    public void Configure(EntityTypeBuilder<ProviderEvse> entity)
    {
        entity.HasOne<ProviderCharger>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ProviderChargerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Evse>()
            .WithMany()
            .HasForeignKey(mapping => mapping.EvseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => new { mapping.ProviderChargerId, mapping.ExternalId }).IsUnique();
    }
}
