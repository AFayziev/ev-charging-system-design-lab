using EvCharging.Domain.Charging;
using EvCharging.Persistence;

namespace EvCharging.ChargingWorker.Handlers;

internal static class StaleSessionQueryExtensions
{
    /// <summary>
    /// Active or stopping sessions whose newest reading is older than <paramref name="staleBefore"/>. A session that
    /// never reported is stale from its start.
    /// </summary>
    public static IQueryable<ChargingSession> StaleSessions(
        this PlatformDbContext database,
        DateTimeOffset staleBefore) =>
        database.Sessions.Where(session =>
            (session.Lifecycle == SessionLifecycle.Active || session.Lifecycle == SessionLifecycle.Stopping)
            && (database.SessionEvidence
                .Where(evidence => evidence.SessionId == session.Id)
                .Select(evidence => evidence.LastObservedAt)
                .FirstOrDefault() ?? session.StartedAt ?? session.CreatedAt) <= staleBefore);
}
