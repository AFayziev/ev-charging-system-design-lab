using EvCharging.Domain.Identity;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.HasPublicId(notification => notification.PublicId);
        entity.Property(notification => notification.RecipientEmail).HasMaxLength(EmailAddress.MaximumLength);
        entity.Property(notification => notification.Subject).HasMaxLength(200);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_notifications_times", "isfinite(prepared_at)");
            table.HasCheckConstraint("ck_notifications_html_body", "html_body IS NULL OR length(html_body) > 0");
            table.HasCheckConstraint(
                "ck_notifications_content",
                "length(recipient_email) > 0 AND length(subject) > 0 AND length(body) > 0");
        });
    }
}
