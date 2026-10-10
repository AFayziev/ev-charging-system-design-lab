using System.Text.Json;
using EvCharging.Persistence.Auditing;

namespace EvCharging.Persistence.Entities;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AuditAction Action { get; set; }
    public AuditResourceType ResourceType { get; set; }
    public long ResourceId { get; set; }
    public AuditActorType ActorType { get; set; }
    public string? ActorIssuer { get; set; }
    public required string ActorSubject { get; set; }
    public JsonElement Details { get; set; }
    public int DetailsVersion { get; set; }
    public string? TraceId { get; set; }
}
