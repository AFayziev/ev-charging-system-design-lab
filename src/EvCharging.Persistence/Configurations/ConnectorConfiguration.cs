using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> entity)
    {
        entity.HasOne<Evse>()
            .WithMany()
            .HasForeignKey(connector => connector.EvseId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(connector => connector.EvseId).IsUnique();
    }
}
