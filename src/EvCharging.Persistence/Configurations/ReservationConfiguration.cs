using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> entity)
    {
        entity.HasAuditResourceType(AuditResourceType.Reservation);
        entity.Property(reservation => reservation.StartsAt).IsAudited();
        entity.Property(reservation => reservation.EndsAt).IsAudited();
        entity.Property(reservation => reservation.State).IsAudited();
        entity.Property(reservation => reservation.ConnectorId).IsAudited();
        entity.Property(reservation => reservation.DriverId).IsAudited();
        entity.Ignore(reservation => reservation.Slot);
        entity.HasPublicId(reservation => reservation.PublicId);
        entity.ToTable(table => table.HasCheckConstraint(
            "ck_reservations_slot", "isfinite(starts_at) AND isfinite(ends_at) AND starts_at < ends_at"));
        entity.HasOne(reservation => reservation.Connector)
            .WithMany()
            .HasForeignKey(reservation => reservation.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(reservation => reservation.Driver)
            .WithMany()
            .HasForeignKey(reservation => reservation.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(reservation => new { reservation.ConnectorId, reservation.StartsAt });
    }
}
