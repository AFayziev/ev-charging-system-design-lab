using EvCharging.Domain.Charging;
using EvCharging.Domain.Identity;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> entity)
    {
        entity.ToTable("operations");
        entity.HasPublicId(operation => operation.PublicId);
        entity.HasAuditResourceType(AuditResourceType.Operation);
        entity.Property(operation => operation.SessionId).IsAudited();
        entity.Property(operation => operation.DriverId).IsAudited();
        entity.Property(operation => operation.Kind).IsAudited();
        entity.Property(operation => operation.Outcome).IsAudited();
        entity.HasOne(operation => operation.Session)
            .WithMany()
            .HasForeignKey(operation => operation.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Driver>()
            .WithMany()
            .HasForeignKey(operation => operation.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(operation => new { operation.DriverId, operation.Kind, operation.IdempotencyKey }).IsUnique();
    }
}
