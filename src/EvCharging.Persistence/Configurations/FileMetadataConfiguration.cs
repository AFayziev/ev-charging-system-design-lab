using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class FileMetadataConfiguration : IEntityTypeConfiguration<FileMetadata>
{
    public void Configure(EntityTypeBuilder<FileMetadata> entity)
    {
        entity.ToTable("file_metadata");
        entity.HasOne<Station>()
            .WithMany()
            .HasForeignKey(file => file.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ChargingSession>()
            .WithMany()
            .HasForeignKey(file => file.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FileMetadata>()
            .WithMany()
            .HasForeignKey(file => file.SourceFileId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(file => new { file.Bucket, file.ObjectKey }).IsUnique();
    }
}
