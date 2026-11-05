using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class ConfirmObservedChargingStartMessageHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    TimeProvider clock,
    ILogger<ConfirmObservedChargingStartMessageHandler> logger)
{
    /// <summary>
    /// Confirms the session's unsettled start from its observations, as a status check would from a provider record.
    /// </summary>
    [NonTransactional]
    public async Task HandleAsync(ConfirmObservedChargingStartMessage message, CancellationToken cancellationToken)
    {
        var unsettledStart = await database.Operations.AsNoTracking()
            .Where(operation => operation.SessionId == message.SessionId
                && operation.Kind == ChargingCommandKind.Start
                && (operation.Outcome == CommandOutcome.Pending || operation.Outcome == CommandOutcome.Uncertain)
                && operation.ReleasedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

        // Completion decides from the session itself whether it can complete.
        bool hasFinalReading = await database.SessionEvidence.AnyAsync(
            evidence => evidence.SessionId == message.SessionId && evidence.FinalEnergyKWh != null,
            cancellationToken);
        var completion = hasFinalReading ? new CompleteChargingSessionMessage(message.SessionId) : null;

        if (unsettledStart is not null)
        {
            await ConfirmFromObservationAsync(unsettledStart, message.ObservedAt, completion, cancellationToken);
        }
        else if (completion is not null)
        {
            outbox.Enroll(database);
            await outbox.PublishAsync(completion);
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
    }

    private async Task ConfirmFromObservationAsync(
        Operation unsettledStart,
        DateTimeOffset observedAt,
        CompleteChargingSessionMessage? completion,
        CancellationToken cancellationToken)
    {
        // Observations match a session through its start requests, so the start has at least one.
        var attempts = await database.Attempts.AsNoTracking()
            .Where(attempt => attempt.OperationId == unsettledStart.Id)
            .OrderBy(attempt => attempt.Number)
            .Select(attempt => new { attempt.AuthorizedAt, attempt.ChargingProviderId })
            .ToListAsync(cancellationToken);
        // The charger cannot have started before it was first asked to, whatever its clock reports.
        var firstRequestedAt = attempts[0].AuthorizedAt;
        var startedAt = observedAt > firstRequestedAt ? observedAt : firstRequestedAt;

        await database.ResolveAsync(
            outbox,
            clock,
            logger,
            unsettledStart,
            operation => operation.ConfirmObservedStart(attempts[^1].ChargingProviderId, startedAt),
            completion,
            cancellationToken);
    }
}
