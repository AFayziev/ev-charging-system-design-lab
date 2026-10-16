using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderConnectorConfiguration : IEntityTypeConfiguration<ProviderConnector>
{
    public void Configure(EntityTypeBuilder<ProviderConnector> entity)
    {
        entity.HasOne(mapping => mapping.ProviderCharger)
            .WithMany()
            .HasForeignKey(mapping => mapping.ProviderChargerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(mapping => mapping.Connector)
            .WithMany()
            .HasForeignKey(mapping => mapping.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => new { mapping.ProviderChargerId, mapping.ExternalEvseId, mapping.ExternalId })
            .IsUnique()
            .AreNullsDistinct(false);
        entity.HasIndex(mapping => mapping.ConnectorId).IsUnique();
    }
}
