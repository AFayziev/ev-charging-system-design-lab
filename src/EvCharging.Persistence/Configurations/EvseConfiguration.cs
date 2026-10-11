using EvCharging.Domain.Equipment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class EvseConfiguration : IEntityTypeConfiguration<Evse>
{
    public void Configure(EntityTypeBuilder<Evse> entity)
    {
        entity.HasPublicId(evse => evse.PublicId);
        entity.HasIndex(evse => new { evse.ChargerId, evse.Number }).IsUnique();

        entity.HasOne(evse => evse.Charger)
            .WithMany(charger => charger.Evses)
            .HasForeignKey(evse => evse.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
