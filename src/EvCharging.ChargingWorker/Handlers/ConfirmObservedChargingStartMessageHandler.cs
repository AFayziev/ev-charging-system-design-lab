using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class ConfirmObservedChargingStartMessageHandler(
    PlatformDbContext database,
    TimeProvider clock,
    ILogger<ConfirmObservedChargingStartMessageHandler> logger)
{
    /// <summary>
    /// Confirms the session's unsettled start from its observations, as a status check would from a provider record.
    /// A final reading recorded while the session was still starting is then handed to completion, so a repeated job
    /// changes nothing.
    /// </summary>
    [NonTransactional]
    public async Task<CompleteChargingSessionMessage?> HandleAsync(
        ConfirmObservedChargingStartMessage message,
        CancellationToken cancellationToken)
    {
        var start = await database.Operations.AsNoTracking()
            .Where(operation => operation.SessionId == message.SessionId
                && operation.Kind == ChargingCommandKind.Start
                && (operation.Outcome == CommandOutcome.Pending || operation.Outcome == CommandOutcome.Uncertain))
            .SingleOrDefaultAsync(cancellationToken);

        if (start is not null)
        {
            await ConfirmAsync(start, message.ObservedAt, cancellationToken);
        }

        // Completion decides from the session itself whether it can complete.
        bool finalised = await database.SessionEvidence.AnyAsync(
            evidence => evidence.SessionId == message.SessionId && evidence.FinalEnergyKWh != null,
            cancellationToken);

        return finalised ? new CompleteChargingSessionMessage(message.SessionId) : null;
    }

    private async Task ConfirmAsync(Operation start, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        // Observations match a session through its start requests, so the start has at least one.
        var requests = await database.Attempts.AsNoTracking()
            .Where(attempt => attempt.OperationId == start.Id)
            .OrderBy(attempt => attempt.Number)
            .Select(attempt => new { attempt.AuthorizedAt, attempt.ChargingProviderId })
            .ToListAsync(cancellationToken);
        // The charger cannot have started before it was first asked to, whatever its clock reports.
        var requestedAt = requests[0].AuthorizedAt;
        var startedAt = observedAt > requestedAt ? observedAt : requestedAt;

        await database.ResolveAsync(
            clock,
            logger,
            start,
            operation => operation.ConfirmObservedStart(requests[^1].ChargingProviderId, startedAt),
            cancellationToken);
    }
}
