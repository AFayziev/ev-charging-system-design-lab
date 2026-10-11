using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ProviderConnectorConfiguration : IEntityTypeConfiguration<ProviderConnector>
{
    public void Configure(EntityTypeBuilder<ProviderConnector> entity)
    {
        entity.HasOne<ProviderEvse>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ProviderEvseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Connector>()
            .WithMany()
            .HasForeignKey(mapping => mapping.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(mapping => new { mapping.ProviderEvseId, mapping.ExternalId }).IsUnique();
    }
}
