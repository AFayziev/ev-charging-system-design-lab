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
        entity.ToTable(table => table.HasCheckConstraint(
            "ck_operations_request_budget",
            "authorized_attempt_count >= 0 AND (request_limit IS NULL AND authorized_attempt_count = 0 "
                + "OR request_limit IS NOT NULL AND request_limit > 0 "
                + "AND authorized_attempt_count <= request_limit)"));
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
        // Serves FindUnreconciledChargingMessageHandler; the filter matches its predicate.
        entity.HasIndex(operation => new { operation.AutomaticChecksEndAt, operation.Id })
            .HasFilter("outcome IN ('pending', 'uncertain') AND authorized_attempt_count > 0");
    }
}
