using EvCharging.Persistence.Auditing;

namespace EvCharging.ChargingWorker.Persistence;

internal sealed class WorkerAuditActorAccessor : IAuditActorAccessor
{
    public AuditActor GetActor() => AuditActor.Service("charging-worker");
}
