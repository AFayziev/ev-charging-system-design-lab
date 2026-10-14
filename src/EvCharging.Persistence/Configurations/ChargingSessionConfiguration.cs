using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public const string ConnectorClaimIndexName = "ux_sessions_connector_claim";
    public const string ReservationClaimIndexName = "ux_sessions_reservation_claim";

    public void Configure(EntityTypeBuilder<ChargingSession> entity)
    {
        entity.ToTable("sessions");
        entity.HasPublicId(session => session.PublicId);
        entity.HasAuditResourceType(AuditResourceType.Session);
        entity.Property(session => session.ReservationId).IsAudited();
        entity.Property(session => session.ConnectorId).IsAudited();
        entity.Property(session => session.Lifecycle).IsAudited();
        entity.HasIndex(session => session.ConnectorId).IsUnique()
            .HasFilter("lifecycle IN (0, 1, 2)")
            .HasDatabaseName(ConnectorClaimIndexName);
        entity.HasIndex(session => session.ReservationId).IsUnique()
            .HasFilter("lifecycle <> 4")
            .HasDatabaseName(ReservationClaimIndexName);
        entity.HasOne(session => session.Reservation)
            .WithMany()
            .HasForeignKey(session => session.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Connector>()
            .WithMany()
            .HasForeignKey(session => session.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ChargingProvider>()
            .WithMany()
            .HasForeignKey(session => session.ChargingProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(session => new { session.ChargingProviderId, session.ProviderSessionId });
    }
}
