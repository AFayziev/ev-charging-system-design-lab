using EvCharging.Contracts.Charging;
using EvCharging.Contracts.Observations;
using EvCharging.Domain.Observations;
using EvCharging.ObservationStream;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.ChargingWorker.Observations;

/// <summary>
/// Records what the observation stream establishes about each charging session. Each batch commits in one
/// transaction. A reading already accounted for changes nothing, so a batch that failed part-way, or is delivered
/// again, converges to the same rows when it is handled again.
/// </summary>
internal sealed class SessionEvidenceBatchHandler(
    IServiceScopeFactory scopes,
    IOptions<ObservationClockSkewOptions> clockSkew,
    ILogger<SessionEvidenceBatchHandler> logger) : IObservationBatchHandler
{
    public async Task HandleAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 0)
        {
            return;
        }

        // A fresh context for every attempt, so a retry never sees the tracked state of a failed one.
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var readings = await MatchSessionsAsync(database, envelopes, cancellationToken);
        if (readings.Count == 0)
        {
            return;
        }

        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox>();
        outbox.Enroll(database);
        var sessionIds = readings.Keys.ToList();
        var evidence = await database.SessionEvidence
            .Where(row => sessionIds.Contains(row.SessionId))
            .ToDictionaryAsync(row => row.SessionId, cancellationToken);
        var advanced = new List<Guid>();
        foreach (var (sessionId, sessionReadings) in readings)
        {
            if (!evidence.TryGetValue(sessionId, out var row))
            {
                row = SessionEvidence.Create(sessionId);
                database.SessionEvidence.Add(row);
            }

            var observedBefore = row.LastObservedAt;
            bool finalised = false;
            foreach (var reading in sessionReadings.OrderBy(envelope => envelope.OccurredAt)
                .ThenBy(envelope => envelope.SourceEventId))
            {
                // The provider's terminal state with its final energy is the evidence that ends a session.
                finalised |= row.Record(
                    reading.OccurredAt,
                    reading.ReceivedAt,
                    reading.EnergyKWh,
                    reading.IsFinalEnergy && reading.State == ObservationState.Stopped,
                    clockSkew.Value.ClockSkew);
            }

            if (row.LastObservedAt != observedBefore)
            {
                advanced.Add(sessionId);
            }

            if (finalised)
            {
                await outbox.PublishAsync(new CompleteChargingSessionMessage(sessionId));
            }
        }

        if (advanced.Count > 0)
        {
            // A session that reported again starts its next stale period with a read as soon as it is due.
            await database.Sessions
                .Where(session => advanced.Contains(session.Id) && session.NextEvidenceReadAt != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(session => session.NextEvidenceReadAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // A reading belongs to a session when its identity is a started session of the same integration.
    private async Task<Dictionary<Guid, List<ObservationEnvelope>>> MatchSessionsAsync(
        PlatformDbContext database,
        IReadOnlyList<ObservationEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var publicIds = envelopes.Select(envelope => envelope.ExternalSessionId).Distinct().ToList();
        var sessionIds = (await database.Attempts
            .Where(attempt => publicIds.Contains(attempt.SessionPublicId))
            .Select(attempt => new { attempt.Operation!.SessionId, attempt.SessionPublicId, attempt.IntegrationKey })
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToDictionary(session => (session.SessionPublicId, session.IntegrationKey), session => session.SessionId);

        var matched = envelopes
            .Where(envelope => sessionIds.ContainsKey((envelope.ExternalSessionId, envelope.IntegrationKey)))
            .ToList();
        if (matched.Count < envelopes.Count)
        {
            logger.LogWarning(
                "{Count} observations match no charging session of their integration and are not recorded.",
                envelopes.Count - matched.Count);
        }

        return matched
            .GroupBy(envelope => sessionIds[(envelope.ExternalSessionId, envelope.IntegrationKey)])
            .ToDictionary(group => group.Key, group => group.ToList());
    }
}
