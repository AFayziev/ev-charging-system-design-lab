using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal sealed class OperatorAssignmentConfiguration : IEntityTypeConfiguration<OperatorAssignment>
{
    public void Configure(EntityTypeBuilder<OperatorAssignment> entity)
    {
        entity.HasKey(assignment => new { assignment.OperatorUserId, assignment.StationId });
        entity.HasOne<OperatorUser>()
            .WithMany()
            .HasForeignKey(assignment => assignment.OperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Station>()
            .WithMany()
            .HasForeignKey(assignment => assignment.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
