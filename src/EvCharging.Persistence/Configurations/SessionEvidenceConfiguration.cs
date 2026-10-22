using EvCharging.Domain.Charging;
using EvCharging.Domain.Observations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class SessionEvidenceConfiguration : IEntityTypeConfiguration<SessionEvidence>
{
    public void Configure(EntityTypeBuilder<SessionEvidence> entity)
    {
        entity.ToTable("session_evidence");
        entity.HasKey(evidence => evidence.SessionId);
        entity.Property(evidence => evidence.Version).IsRowVersion();
        entity.HasOne<ChargingSession>()
            .WithOne()
            .HasForeignKey<SessionEvidence>(evidence => evidence.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
