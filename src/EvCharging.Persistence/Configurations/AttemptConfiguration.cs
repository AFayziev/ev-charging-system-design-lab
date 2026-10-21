using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> entity)
    {
        entity.HasOne(attempt => attempt.Operation)
            .WithMany()
            .HasForeignKey(attempt => attempt.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(attempt => new { attempt.OperationId, attempt.Number }).IsUnique();
    }
}
