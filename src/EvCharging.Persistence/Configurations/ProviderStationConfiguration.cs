using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderStationConfiguration : IEntityTypeConfiguration<ProviderStation>
{
    public void Configure(EntityTypeBuilder<ProviderStation> entity)
    {
        entity.HasOne<ChargingProvider>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ChargingProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Station>()
            .WithMany()
            .HasForeignKey(mapping => mapping.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => new { mapping.ChargingProviderId, mapping.ExternalId }).IsUnique();
    }
}
