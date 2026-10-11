using EvCharging.Domain.Equipment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ChargerConfiguration : IEntityTypeConfiguration<Charger>
{
    public void Configure(EntityTypeBuilder<Charger> entity)
    {
        entity.HasPublicId(charger => charger.PublicId);

        entity.HasOne(charger => charger.Station)
            .WithMany(station => station.Chargers)
            .HasForeignKey(charger => charger.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
