using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> entity)
    {
        entity.Property(connector => connector.PublicId).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(connector => connector.PublicId).IsUnique();

        entity.HasOne(connector => connector.Evse)
            .WithMany(evse => evse.Connectors)
            .HasForeignKey(connector => connector.EvseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(connector => connector.EvseId).IsUnique();
    }
}
