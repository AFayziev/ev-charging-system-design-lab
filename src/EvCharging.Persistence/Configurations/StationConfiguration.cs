using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Auditing;
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

        entity.HasPublicId(station => station.PublicId);
        entity.Property(station => station.Version).IsRowVersion();
        entity.Property(station => station.GalleryVersion).IsConcurrencyToken();
        entity.ToTable(table => table.HasCheckConstraint("ck_stations_gallery_version", "gallery_version >= 0"));
    }
}
