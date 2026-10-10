using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> entity)
    {
        entity.Property(audit => audit.Action).HasConversion<string>();
        entity.Property(audit => audit.ResourceType).HasConversion<string>();
        entity.Property(audit => audit.ActorType).HasConversion<string>();
        entity.Property(audit => audit.Details).HasColumnType("jsonb");
        entity.HasIndex(audit => new { audit.ResourceType, audit.ResourceId, audit.OccurredAt, audit.Id });
    }
}
