using EvCharging.ChargingWorker.Configuration;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

/// <summary>Requests a provider snapshot for each active or stopping session whose newest reading is stale.</summary>
public static class FindStaleSessionsMessageHandler
{
    [Transactional]
    public static async Task<OutgoingMessages> HandleAsync(
        FindStaleSessionsMessage message,
        PlatformDbContext database,
        TimeProvider clock,
        IOptions<SessionEvidenceRefreshOptions> refreshOptions,
        IOptions<ChargingFreshnessOptions> freshnessOptions,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var refresh = refreshOptions.Value;
        var staleBefore = now - freshnessOptions.Value.FreshnessLimit;
        var due = await DueSessions(database, now, staleBefore)
            .OrderBy(session => session.NextEvidenceReadAt)
            .ThenBy(session => session.Id)
            .Select(session => new
                {
                    session.Id,
                    ObservedAt = database.SessionEvidence
                        .Where(evidence => evidence.SessionId == session.Id)
                        .Select(evidence => evidence.LastObservedAt)
                        .FirstOrDefault() ?? session.StartedAt ?? session.CreatedAt
                })
            .Take(refresh.BatchSize)
            .ToListAsync(cancellationToken);

        var outgoing = new OutgoingMessages();
        foreach (var session in due)
        {
            var readAt = now + NextReadDelay(now - session.ObservedAt, refresh);
            // Concurrent scans claim each session once.
            int claimed = await DueSessions(database, now, staleBefore)
                .Where(value => value.Id == session.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.NextEvidenceReadAt, readAt),
                    cancellationToken);
            if (claimed == 1)
            {
                outgoing.Add(new RefreshSessionEvidenceMessage(session.Id));
            }
        }

        return outgoing;
    }

    /// <summary>A quarter of the time stale, within the read interval limits.</summary>
    private static TimeSpan NextReadDelay(TimeSpan staleFor, SessionEvidenceRefreshOptions options) =>
        TimeSpan.FromTicks(Math.Clamp(
            staleFor.Ticks / 4,
            options.MinimumReadInterval.Ticks,
            options.MaximumReadInterval.Ticks));

    private static IQueryable<ChargingSession> DueSessions(
        PlatformDbContext database,
        DateTimeOffset now,
        DateTimeOffset staleBefore) =>
        database.StaleSessions(staleBefore)
            .Where(session => session.NextEvidenceReadAt == null || session.NextEvidenceReadAt <= now);
}
