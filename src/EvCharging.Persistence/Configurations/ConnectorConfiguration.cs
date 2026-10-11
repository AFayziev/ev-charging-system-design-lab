using EvCharging.Domain.Equipment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> entity)
    {
        entity.HasPublicId(connector => connector.PublicId);

        entity.HasOne(connector => connector.Evse)
            .WithMany(evse => evse.Connectors)
            .HasForeignKey(connector => connector.EvseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(connector => connector.EvseId).IsUnique();
    }
}
