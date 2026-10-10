using System.Diagnostics;
using System.Text.Json;
using EvCharging.Persistence.Entities;

namespace EvCharging.Persistence.Auditing;

public sealed class AuditEntryWriter(
    IAuditActorAccessor actors,
    TimeProvider timeProvider)
{
    /// <summary>Stages an audit in the caller's transaction; the caller owns SaveChanges.</summary>
    public AuditEntry Append<TDetails>(
        PlatformDbContext database,
        AuditAction action,
        AuditResourceType resourceType,
        long resourceId,
        TDetails details,
        int detailsVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detailsVersion);
        var actor = actors.GetActor();
        var audit = new AuditEntry
        {
            OccurredAt = timeProvider.GetUtcNow(),
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorType = actor.Type,
            ActorIssuer = actor.Issuer,
            ActorSubject = actor.Subject,
            Details = JsonSerializer.SerializeToElement(details, JsonSerializerOptions.Web),
            DetailsVersion = detailsVersion,
            TraceId = Activity.Current?.TraceId.ToString()
        };
        database.AuditEntries.Add(audit);
        return audit;
    }
}
