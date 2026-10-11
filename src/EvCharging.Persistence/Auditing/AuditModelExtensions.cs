using EvCharging.Domain.Auditing;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvCharging.Persistence.Auditing;

internal static class AuditModelExtensions
{
    private const string ResourceTypeAnnotation = "Audit:ResourceType";
    private const string IncludedPropertyAnnotation = "Audit:Included";

    public static PropertyBuilder<TProperty> IsAudited<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasAnnotation(IncludedPropertyAnnotation, true);

    public static bool IsAudited(this IReadOnlyProperty property) =>
        property.FindAnnotation(IncludedPropertyAnnotation)?.Value is true;

    public static EntityTypeBuilder<TEntity> HasAuditResourceType<TEntity>(
        this EntityTypeBuilder<TEntity> entity, AuditResourceType resourceType)
        where TEntity : class, IAuditableEntity =>
        entity.HasAnnotation(ResourceTypeAnnotation, resourceType.ToString());

    public static AuditResourceType GetAuditResourceType(this IReadOnlyEntityType entity)
    {
        if (entity.FindAnnotation(ResourceTypeAnnotation)?.Value is not string name
            || !Enum.TryParse<AuditResourceType>(name, out var resourceType))
        {
            throw new InvalidOperationException($"An audit resource type is required for {entity.Name}.");
        }

        return resourceType;
    }
}
