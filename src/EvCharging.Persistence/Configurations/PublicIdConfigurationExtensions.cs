using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Configurations;

internal static class PublicIdConfigurationExtensions
{
    public static EntityTypeBuilder<TEntity> HasPublicId<TEntity>(
        this EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, Guid>> property)
        where TEntity : class
    {
        var publicId = entity.Property(property).HasDefaultValueSql("gen_random_uuid()");
        entity.HasIndex(publicId.Metadata.Name).IsUnique();
        return entity;
    }
}
