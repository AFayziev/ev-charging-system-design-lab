using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class StationMediaItemConfiguration : IEntityTypeConfiguration<StationMediaItem>
{
    internal const string PositionIndexName = "ix_station_media_items_station_id_position";

    public void Configure(EntityTypeBuilder<StationMediaItem> entity)
    {
        entity.HasAuditResourceType(AuditResourceType.StationMediaItem);
        entity.Property(item => item.StationId).IsAudited();
        entity.Property(item => item.Kind).IsAudited();
        entity.Property(item => item.Position).IsAudited();
        entity.HasPublicId(item => item.PublicId);
        entity.HasOne(item => item.Station)
            .WithMany()
            .HasForeignKey(item => item.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasMany(item => item.Versions)
            .WithOne(version => version.Item)
            .HasForeignKey(version => version.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(item => new { item.StationId, item.Position })
            .IsUnique()
            .HasDatabaseName(PositionIndexName);
        entity.ToTable(table => table.HasCheckConstraint("ck_station_media_items_position", "position >= 0"));
    }
}
