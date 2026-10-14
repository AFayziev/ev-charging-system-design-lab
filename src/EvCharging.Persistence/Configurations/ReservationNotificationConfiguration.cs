using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class ReservationNotificationConfiguration : IEntityTypeConfiguration<ReservationNotification>
{
    internal const string ReservationKindIndexName = "ix_reservation_notifications_reservation_id_kind";

    public void Configure(EntityTypeBuilder<ReservationNotification> entity)
    {
        entity.HasOne<Reservation>()
            .WithMany()
            .HasForeignKey(value => value.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Notification>()
            .WithOne()
            .HasForeignKey<ReservationNotification>(value => value.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => new { value.ReservationId, value.Kind })
            .IsUnique()
            .HasDatabaseName(ReservationKindIndexName);
    }
}
