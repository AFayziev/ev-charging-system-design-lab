using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ChargerConfiguration : IEntityTypeConfiguration<Charger>
{
    public void Configure(EntityTypeBuilder<Charger> entity)
    {
        entity.HasOne<Station>()
            .WithMany()
            .HasForeignKey(charger => charger.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
