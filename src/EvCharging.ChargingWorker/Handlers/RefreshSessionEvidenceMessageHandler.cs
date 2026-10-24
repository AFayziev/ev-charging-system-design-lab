using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Domain.Charging;
using EvCharging.ObservationStream;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

/// <summary>
/// Reads the provider's latest observation of a stale session and appends it to the observation stream, where it is
/// recorded like a callback. A failed read or append is retried when the session is next due.
/// </summary>
public sealed class RefreshSessionEvidenceMessageHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    IObservationStreamProducer stream,
    TimeProvider clock,
    IOptions<ChargingFreshnessOptions> freshnessOptions,
    ILogger<RefreshSessionEvidenceMessageHandler> logger)
{
    [NonTransactional]
    public async Task HandleAsync(RefreshSessionEvidenceMessage message, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // The session may have reported or ended since the scan queued this read.
        bool stale = await database.StaleSessions(now - freshnessOptions.Value.FreshnessLimit)
            .AnyAsync(session => session.Id == message.SessionId, cancellationToken);
        if (!stale)
        {
            return;
        }

        var started = await database.Attempts.AsNoTracking()
            .Where(attempt => attempt.Operation!.SessionId == message.SessionId
                && attempt.Operation.Kind == ChargingCommandKind.Start)
            .OrderByDescending(attempt => attempt.Number)
            .Select(attempt => new { attempt.IntegrationKey, attempt.SessionPublicId })
            .FirstOrDefaultAsync(cancellationToken);
        if (started is null)
        {
            return;
        }

        using var provider = providers.Create(started.IntegrationKey);
        var snapshot = await provider.GetSessionSnapshotAsync(started.SessionPublicId, cancellationToken);
        if (snapshot.IsFailed)
        {
            logger.LogInformation("The snapshot of session {SessionId} could not be read.", message.SessionId);
            return;
        }

        if (snapshot.Value is not { } reading)
        {
            logger.LogInformation("The provider has no observation of session {SessionId}.", message.SessionId);
            return;
        }

        var envelope = reading.ToEnvelope(started.IntegrationKey, Guid.CreateVersion7(), now);
        try
        {
            await stream.AppendAsync([envelope], cancellationToken);
        }
        catch (ObservationStreamException exception)
        {
            logger.LogWarning(
                exception,
                "The snapshot of session {SessionId} was not confirmed by the stream.",
                message.SessionId);
        }
    }
}
