using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ChargerConfiguration : IEntityTypeConfiguration<Charger>
{
    public void Configure(EntityTypeBuilder<Charger> entity)
    {
        entity.Property(charger => charger.PublicId).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(charger => charger.PublicId).IsUnique();

        entity.HasOne(charger => charger.Station)
            .WithMany(station => station.Chargers)
            .HasForeignKey(charger => charger.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
