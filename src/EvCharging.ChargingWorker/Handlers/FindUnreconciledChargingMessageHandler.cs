using EvCharging.ChargingWorker.Configuration;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

/// <summary>Sends another status check for each unresolved operation whose checking window has closed.</summary>
public static class FindUnreconciledChargingMessageHandler
{
    [Transactional]
    public static async Task<OutgoingMessages> HandleAsync(
        FindUnreconciledChargingMessage message,
        PlatformDbContext database,
        TimeProvider clock,
        IOptions<ChargingExecutionOptions> options,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var policy = options.Value;
        // At most one check per listener slot, so each claimed check runs within its window.
        var due = await UnreconciledOperationsQuery(database.Operations, now)
            .OrderBy(operation => operation.AutomaticChecksEndAt)
            .ThenBy(operation => operation.Id)
            .Select(operation => new { operation.Id, operation.Kind, operation.AcceptedAt })
            .Take(policy.MaximumConcurrentCommands)
            .ToListAsync(cancellationToken);

        var outgoing = new OutgoingMessages();
        foreach (var operation in due)
        {
            var checkUntil = now + NextCheckDelay(now - operation.AcceptedAt, policy);
            // Concurrent scans claim each operation once.
            int claimed = await UnreconciledOperationsQuery(database.Operations, now)
                .Where(value => value.Id == operation.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.AutomaticChecksEndAt, checkUntil),
                    cancellationToken);
            if (claimed == 1)
            {
                outgoing.Add(operation.Kind == ChargingCommandKind.Start
                    ? new CheckChargingStartMessage(operation.Id, Automatic: true)
                    : new CheckChargingStopMessage(operation.Id));
            }
        }

        return outgoing;
    }

    /// <summary>A quarter of the time unresolved, within the continued check interval limits.</summary>
    private static TimeSpan NextCheckDelay(TimeSpan unresolvedFor, ChargingExecutionOptions policy) =>
        TimeSpan.FromTicks(Math.Clamp(
            unresolvedFor.Ticks / 4,
            policy.ContinuedStatusCheckInterval.Ticks,
            policy.MaximumContinuedStatusCheckInterval.Ticks));

    private static IQueryable<Operation> UnreconciledOperationsQuery(
        IQueryable<Operation> operations,
        DateTimeOffset now) =>
        operations.Where(operation =>
            (operation.Outcome == CommandOutcome.Pending || operation.Outcome == CommandOutcome.Uncertain)
            && operation.AuthorizedAttemptCount > 0
            && operation.AutomaticChecksEndAt <= now);
}
