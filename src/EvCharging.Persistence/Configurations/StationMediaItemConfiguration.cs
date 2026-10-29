using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class StationMediaItemConfiguration : IEntityTypeConfiguration<StationMediaItem>
{
    public void Configure(EntityTypeBuilder<StationMediaItem> entity)
    {
        entity.HasAuditResourceType(AuditResourceType.StationMediaItem);
        entity.Property(item => item.StationId).IsAudited();
        entity.Property(item => item.Kind).IsAudited();
        entity.Property(item => item.Position).IsAudited();
        entity.Property(item => item.ReplacesItemId).IsAudited();
        entity.Property(item => item.State).IsAudited();
        entity.Property(item => item.ContentType).IsAudited().HasMaxLength(100);
        entity.Property(item => item.ByteLength).IsAudited();
        entity.Property(item => item.UploadExpiresAt).IsAudited();
        entity.Property(item => item.PublishedAt).IsAudited();
        entity.Property(item => item.FailureCode).IsAudited().HasMaxLength(50);
        entity.Property(item => item.RowVersion).IsRowVersion();
        entity.HasPublicId(item => item.PublicId);
        entity.HasOne(item => item.Station)
            .WithMany()
            .HasForeignKey(item => item.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Removing a photo removes the replacement waiting to take its place.
        entity.HasOne<StationMediaItem>()
            .WithMany()
            .HasForeignKey(item => item.ReplacesItemId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(item => item.StationId);
        entity.HasIndex(item => item.ReplacesItemId).IsUnique();
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_station_media_items_position", "position >= 0");
            table.HasCheckConstraint(
                "ck_station_media_items_replacement_position", "(position IS NULL) = (replaces_item_id IS NOT NULL)");
            table.HasCheckConstraint("ck_station_media_items_byte_length", "byte_length > 0");
            table.HasCheckConstraint(
                "ck_station_media_items_upload_expires_at", "isfinite(upload_expires_at)");
            table.HasCheckConstraint(
                "ck_station_media_items_published_at",
                "(state = 'published') = (published_at IS NOT NULL) AND (published_at IS NULL OR isfinite(published_at))");
            table.HasCheckConstraint(
                "ck_station_media_items_failure_code",
                $"(state = 'failed') = (failure_code IS NOT NULL) AND (failure_code IS NULL OR failure_code IN ({string.Join(", ", StationMediaFailure.Codes.Select(code => $"'{code}'"))}))");
        });
    }
}
