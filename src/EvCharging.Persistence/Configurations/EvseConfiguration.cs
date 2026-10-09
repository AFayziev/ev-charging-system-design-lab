using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class EvseConfiguration : IEntityTypeConfiguration<Evse>
{
    public void Configure(EntityTypeBuilder<Evse> entity)
    {
        entity.HasOne<Charger>()
            .WithMany()
            .HasForeignKey(evse => evse.ChargerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
