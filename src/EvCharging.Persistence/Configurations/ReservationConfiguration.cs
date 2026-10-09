using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> entity)
    {
        entity.HasOne<Connector>()
            .WithMany()
            .HasForeignKey(reservation => reservation.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Driver>()
            .WithMany()
            .HasForeignKey(reservation => reservation.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(reservation => new { reservation.ConnectorId, reservation.StartsAt });
    }
}
