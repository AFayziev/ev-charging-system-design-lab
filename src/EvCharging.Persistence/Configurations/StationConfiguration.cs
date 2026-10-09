using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> entity)
    {
        entity.Property(station => station.PublicId).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(station => station.PublicId).IsUnique();
    }
}
