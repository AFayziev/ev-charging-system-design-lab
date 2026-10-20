using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class CheckChargingStopCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IMessageBus bus,
    ILogger<CheckChargingStopCommandHandler> logger)
{
    [NonTransactional]
    public Task<ChargingCheckResult> HandleAsync(CheckChargingStop message, CancellationToken cancellationToken) =>
        CommandExecution.RunAutomaticCheckAsync(
            database,
            clock,
            message.OperationId,
            token => CheckAsync(message.OperationId, token),
            cancellationToken);

    private async Task<ChargingCheckResult> CheckAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == operationId && value.Kind == ChargingCommandKind.Stop, cancellationToken);
        if (operation is null)
        {
            return new(ChargingCheckOutcome.Unresolved);
        }

        if (operation.Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected)
        {
            return new(ChargingCheckOutcome.Resolved);
        }

        var attempt = await database.Attempts.AsNoTracking().Where(value => value.OperationId == operation.Id)
            .OrderByDescending(value => value.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            return new(ChargingCheckOutcome.Unresolved);
        }

        // Only the stop's own command record is evidence. An ended session does not show that this stop did it.
        using var provider = providers.Create(attempt.IntegrationKey);
        var lookup = await provider.GetCommandByExternalOperationIdAsync(attempt.OperationPublicId, cancellationToken);
        if (lookup.IsFailed
            || lookup.Value is { } other
            && (other.ExternalOperationId != attempt.OperationPublicId || other.Kind != ProviderCommandKind.Stop))
        {
            return Unresolved(operation, ChargingCheckOutcome.Unavailable);
        }

        if (lookup.Value is not { } command)
        {
            await CommandExecution.ResendAsync(
                bus, logger, operation, attempt, number => new StopCharging(operation.Id, number));
            return Unresolved(operation, ChargingCheckOutcome.Unresolved);
        }

        await CommandExecution.ResolveAsync(
            database,
            logger,
            operation,
            unresolved => command.Outcome == ProviderCommandOutcome.Rejected
                ? unresolved.RejectStop()
                : unresolved.ConfirmStop(),
            cancellationToken);
        return new(ChargingCheckOutcome.Resolved);
    }

    private ChargingCheckResult Unresolved(Operation operation, ChargingCheckOutcome outcome)
    {
        logger.LogInformation(
            "Status check for stop {OperationId} returned {CheckOutcome}", operation.PublicId, outcome);
        return new(outcome);
    }
}
