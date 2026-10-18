using EvCharging.Domain.Equipment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderChargerConfiguration : IEntityTypeConfiguration<ProviderCharger>
{
    public void Configure(EntityTypeBuilder<ProviderCharger> entity)
    {
        entity.HasOne(mapping => mapping.ProviderStation)
            .WithMany()
            .HasForeignKey(mapping => mapping.ProviderStationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(mapping => mapping.Charger)
            .WithOne(charger => charger.ProviderCharger)
            .HasForeignKey<ProviderCharger>(mapping => mapping.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => mapping.ChargerId).IsUnique();
        entity.HasIndex(mapping => new { mapping.ProviderStationId, mapping.ExternalId }).IsUnique();
    }
}
