using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class NotificationDeliveryAttemptConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> entity)
    {
        entity.HasOne(attempt => attempt.Notification).WithMany().HasForeignKey(attempt => attempt.NotificationId);
        entity.HasIndex(attempt => new { attempt.NotificationId, attempt.Number }).IsUnique();
        entity.Property(attempt => attempt.Provider).HasMaxLength(40);
        entity.Property(attempt => attempt.ExternalMessageId).HasMaxLength(200);
        entity.Property(attempt => attempt.FailureCode).HasMaxLength(100);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_notification_delivery_attempts_number", "number > 0");
            table.HasCheckConstraint("ck_notification_delivery_attempts_provider", "length(provider) > 0");
            table.HasCheckConstraint(
                "ck_notification_delivery_attempts_times",
                "isfinite(started_at) AND (completed_at IS NULL "
                + "OR (isfinite(completed_at) AND completed_at >= started_at))");
            table.HasCheckConstraint(
                "ck_notification_delivery_attempts_evidence",
                "outcome = 'uncertain' OR completed_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_notification_delivery_attempts_capture",
                "(outcome = 'captured') = (external_message_id IS NOT NULL) "
                + "AND (external_message_id IS NULL OR length(external_message_id) > 0)");
        });
    }
}
