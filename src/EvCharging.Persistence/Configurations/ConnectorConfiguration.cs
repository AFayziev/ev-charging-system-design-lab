using EvCharging.Domain.Equipment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> entity)
    {
        entity.HasPublicId(connector => connector.PublicId);

        entity.HasOne(connector => connector.Charger)
            .WithMany(charger => charger.Connectors)
            .HasForeignKey(connector => connector.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(connector => new { connector.ChargerId, connector.Number }).IsUnique();
    }
}
