using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderChargerConfiguration : IEntityTypeConfiguration<ProviderCharger>
{
    public void Configure(EntityTypeBuilder<ProviderCharger> entity)
    {
        entity.HasOne<ProviderStation>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ProviderStationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Charger>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => mapping.ChargerId).IsUnique();
        entity.HasIndex(mapping => new { mapping.ProviderStationId, mapping.ExternalId }).IsUnique();
    }
}
