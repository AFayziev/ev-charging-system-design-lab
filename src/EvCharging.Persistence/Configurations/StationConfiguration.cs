using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> entity)
    {
        entity.HasAuditResourceType(AuditResourceType.Station);
        entity.Property(station => station.Name).IsAudited();
        entity.Property(station => station.Address).IsAudited();
        entity.Property(station => station.AccessInstructions).IsAudited();

        entity.Property(station => station.PublicId).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(station => station.PublicId).IsUnique();
        entity.Property(station => station.Version).IsRowVersion();
    }
}
