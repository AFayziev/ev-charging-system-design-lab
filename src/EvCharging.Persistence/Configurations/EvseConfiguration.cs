using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class EvseConfiguration : IEntityTypeConfiguration<Evse>
{
    public void Configure(EntityTypeBuilder<Evse> entity)
    {
        entity.Property(evse => evse.PublicId).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(evse => evse.PublicId).IsUnique();
        entity.HasIndex(evse => new { evse.ChargerId, evse.Number }).IsUnique();

        entity.HasOne(evse => evse.Charger)
            .WithMany(charger => charger.Evses)
            .HasForeignKey(evse => evse.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
