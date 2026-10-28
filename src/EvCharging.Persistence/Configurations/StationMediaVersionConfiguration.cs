using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class StationMediaVersionConfiguration : IEntityTypeConfiguration<StationMediaVersion>
{
    public void Configure(EntityTypeBuilder<StationMediaVersion> entity)
    {
        entity.HasAuditResourceType(AuditResourceType.StationMediaVersion);
        entity.Property(version => version.ItemId).IsAudited();
        entity.Property(version => version.State).IsAudited();
        entity.Property(version => version.ContentType).IsAudited().HasMaxLength(100);
        entity.Property(version => version.ByteLength).IsAudited();
        entity.Property(version => version.UploadExpiresAt).IsAudited();
        entity.Property(version => version.UploadedAt).IsAudited();
        entity.Property(version => version.RowVersion).IsRowVersion();
        entity.HasPublicId(version => version.PublicId);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_station_media_versions_byte_length", "byte_length > 0");
            table.HasCheckConstraint(
                "ck_station_media_versions_upload_expires_at", "isfinite(upload_expires_at)");
            table.HasCheckConstraint(
                "ck_station_media_versions_uploaded_at",
                "(state = 'uploaded') = (uploaded_at IS NOT NULL) AND (uploaded_at IS NULL OR isfinite(uploaded_at))");
        });
    }
}
