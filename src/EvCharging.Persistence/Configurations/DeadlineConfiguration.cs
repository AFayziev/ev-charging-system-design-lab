using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class DeadlineConfiguration : IEntityTypeConfiguration<Deadline>
{
    public void Configure(EntityTypeBuilder<Deadline> entity)
    {
        entity.HasOne<Reservation>()
            .WithMany()
            .HasForeignKey(deadline => deadline.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Operation>()
            .WithMany()
            .HasForeignKey(deadline => deadline.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ChargingSession>()
            .WithMany()
            .HasForeignKey(deadline => deadline.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(deadline => deadline.DueAt).HasFilter("handled_at IS NULL");
    }
}
