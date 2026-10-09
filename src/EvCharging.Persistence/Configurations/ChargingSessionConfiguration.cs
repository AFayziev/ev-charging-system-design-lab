using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> entity)
    {
        entity.ToTable("sessions");
        entity.HasOne<Reservation>()
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
