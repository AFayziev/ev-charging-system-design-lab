using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class StopChargingCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IOptions<ChargingExecutionOptions> options,
    IDbContextOutbox outbox,
    ILogger<StopChargingCommandHandler> logger)
{
    [NonTransactional]
    public async Task HandleAsync(StopCharging message, CancellationToken cancellationToken)
    {
        var attempt = await AuthorizeAsync(message, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        // Authorization has committed and its transaction has been disposed before any HTTP work.
        using var provider = providers.Create(attempt.IntegrationKey);
        var result = await provider.StopAsync(attempt.OperationPublicId, attempt.SessionPublicId, cancellationToken);
        database.ChangeTracker.Clear();
        await CommandExecution.ApplyOutcomeAsync(
            database,
            clock,
            logger,
            attempt,
            result,
            (operation, evidence) => evidence switch
            {
                ProviderEvidence.Applied => operation.ConfirmStop(),
                ProviderEvidence.Rejected => operation.RejectStop(),
                _ => operation.MarkUncertain()
            },
            cancellationToken);
    }

    private async Task<Attempt?> AuthorizeAsync(StopCharging message, CancellationToken cancellationToken)
    {
        var operationId = message.OperationId;
        int attemptNumber = message.AttemptNumber;
        bool isRetry = attemptNumber > 1;
        Attempt? Skipped(string reason)
        {
            logger.LogInformation(
                "Stop request {AttemptNumber} for operation {OperationId} was not authorized: {Reason}",
                attemptNumber,
                operationId,
                reason);
            return null;
        }

        var driverId = await database.Operations.Where(value => value.Id == operationId)
            .Select(value => (Guid?)value.DriverId)
            .SingleOrDefaultAsync(cancellationToken);
        if (driverId is null)
        {
            return Skipped("the operation does not exist");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId.Value, cancellationToken);
        var operation = await database.LoadOperationAsync(operationId, cancellationToken);
        // A message authorizes only its own numbered attempt, so redelivery cannot spend extra budget.
        if (operation is null || operation.Kind != ChargingCommandKind.Stop
            || operation.AuthorizedAttemptCount != attemptNumber - 1
            || operation.Outcome != (isRetry ? CommandOutcome.Uncertain : CommandOutcome.Pending)
            || isRetry && operation.AuthorizedAttemptCount >= operation.RequestLimit)
        {
            return Skipped("the operation is not awaiting this request");
        }

        if (isRetry && await IsPreviousAttemptUnfinishedAsync(operation, cancellationToken))
        {
            return Skipped("the previous request has not finished");
        }

        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        if (session.Lifecycle != SessionLifecycle.Stopping || session.ChargingProviderId is not { } providerId)
        {
            return Skipped("the session is no longer stopping");
        }

        // The stop goes where the session started: the provider binding saved with its start request, never
        // the connector's current mapping.
        var started = await database.Attempts.AsNoTracking()
            .Where(value => database.Operations.Any(start => start.Id == value.OperationId
                && start.SessionId == session.Id && start.Kind == ChargingCommandKind.Start))
            .OrderByDescending(value => value.Number)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The session has no recorded start request.");
        providers.ValidateIntegration(started.IntegrationKey);

        return await CommandExecution.AuthorizeAttemptAsync(
            database,
            transaction,
            outbox,
            options.Value,
            operation,
            attemptNumber,
            new ProviderBinding(
                providerId,
                started.IntegrationKey,
                new(started.ProviderStationId, started.ProviderChargerId, started.ProviderConnectorId)),
            () => new CheckChargingStop(operation.Id),
            clock.GetUtcNow(),
            logger,
            cancellationToken);
    }

    private Task<bool> IsPreviousAttemptUnfinishedAsync(Operation operation, CancellationToken cancellationToken) =>
        database.Attempts.AsNoTracking().AnyAsync(
            value => value.OperationId == operation.Id && value.Number == operation.AuthorizedAttemptCount
                && value.FinishedAt == null,
            cancellationToken);
}
