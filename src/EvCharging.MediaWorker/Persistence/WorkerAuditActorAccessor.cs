using EvCharging.Persistence.Auditing;

namespace EvCharging.MediaWorker.Persistence;

internal sealed class WorkerAuditActorAccessor : IAuditActorAccessor
{
    public AuditActor GetActor() => AuditActor.Service("media-worker");
}
