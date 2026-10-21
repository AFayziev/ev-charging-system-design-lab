using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EvCharging.ObservationWorker.Projection;

/// <summary>
/// Projects accepted observations into the history and latest views. Every write is idempotent, so a batch that
/// failed part-way, or is delivered again, converges to the same documents when it is projected again.
/// </summary>
internal sealed class ObservationProjector(
    ObservationStore store,
    IOptions<ObservationProcessingOptions> options,
    ILogger<ObservationProjector> logger)
{
    public async Task ProjectAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 0)
        {
            return;
        }

        // Independent and idempotent: the latest view is applied even when history already holds a reading, so a
        // crash between the two writes is repaired by redelivery.
        await Task.WhenAll(
            InsertHistoryAsync(envelopes, cancellationToken),
            ApplyLatestAsync(envelopes, cancellationToken));
    }

    private async Task InsertHistoryAsync(
        IReadOnlyList<ObservationEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.History.InsertManyAsync(
                envelopes.Select(ObservationHistoryDocument.From),
                new InsertManyOptions { IsOrdered = false },
                cancellationToken);
        }
        catch (MongoBulkWriteException exception) when (IsOnlyDuplicates(exception))
        {
            // The events are already recorded.
        }
    }

    private async Task ApplyLatestAsync(
        IReadOnlyList<ObservationEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var tolerance = options.Value.FutureTolerance;
        var filters = Builders<SessionLatestDocument>.Filter;
        var current = envelopes.Where(envelope => envelope.OccurredAt <= envelope.ReceivedAt + tolerance).ToList();
        if (current.Count < envelopes.Count)
        {
            // A reading dated ahead of its receipt would otherwise outrank every later, correct reading.
            logger.LogWarning(
                "{Count} observations are dated more than {Tolerance} ahead of their receipt and stay history only.",
                envelopes.Count - current.Count,
                tolerance);
        }

        var replacements = current
            .GroupBy(envelope => (envelope.IntegrationKey, envelope.ExternalSessionId))
            // The store keeps milliseconds, so readings are ranked at that precision however the batches fall.
            .Select(session => SessionLatestDocument.From(
                session.MaxBy(envelope => envelope.OccurredAt.ToUnixTimeMilliseconds())!))
            .Select(document => new ReplaceOneModel<SessionLatestDocument>(
                filters.Eq(latest => latest.Id, document.Id)
                    & filters.Lt(latest => latest.Reading.OccurredAt, document.Reading.OccurredAt),
                document)
            { IsUpsert = true })
            .ToList();
        if (replacements.Count == 0)
        {
            return;
        }

        try
        {
            await store.Latest.BulkWriteAsync(
                replacements, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
        }
        catch (MongoBulkWriteException<SessionLatestDocument> exception) when (IsOnlyDuplicates(exception))
        {
            // A stored reading with the same or a newer occurrence time is current; this one stays history.
        }
    }

    private static bool IsOnlyDuplicates(MongoBulkWriteException exception) =>
        exception.WriteConcernError is null
        && exception.WriteErrors.All(error => error.Category == ServerErrorCategory.DuplicateKey);
}
