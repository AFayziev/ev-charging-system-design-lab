using EvCharging.Domain.Identity;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> entity)
    {
        entity.HasOne<ChargingSession>()
            .WithMany()
            .HasForeignKey(operation => operation.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Driver>()
            .WithMany()
            .HasForeignKey(operation => operation.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(operation => new { operation.DriverId, operation.Kind, operation.IdempotencyKey });
    }
}
