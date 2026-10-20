using System.Globalization;
using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class StartChargingCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IOptions<ChargingExecutionOptions> options,
    IDbContextOutbox outbox,
    ILogger<StartChargingCommandHandler> logger)
{
    [NonTransactional]
    public async Task HandleAsync(StartCharging message, CancellationToken cancellationToken)
    {
        var attempt = await AuthorizeAsync(message, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        // Authorization has committed and its transaction has been disposed before any HTTP work.
        using var provider = providers.Create(attempt.IntegrationKey);
        var result = await provider.StartAsync(
            attempt.OperationPublicId,
            attempt.SessionPublicId,
            new(attempt.ProviderStationId, attempt.ProviderChargerId, attempt.ProviderConnectorId),
            cancellationToken);
        database.ChangeTracker.Clear();
        await ApplyOutcomeAsync(attempt, result, cancellationToken);
    }

    private async Task<Attempt?> AuthorizeAsync(StartCharging message, CancellationToken cancellationToken)
    {
        var operationId = message.OperationId;
        int attemptNumber = message.AttemptNumber;
        bool isRetry = attemptNumber > 1;
        Attempt? Skipped(string reason)
        {
            logger.LogInformation(
                "Start request {AttemptNumber} for operation {OperationId} was not authorized: {Reason}",
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
        if (operation is null || operation.Kind != ChargingCommandKind.Start
            || operation.AuthorizedAttemptCount != attemptNumber - 1
            || operation.Outcome != (isRetry ? CommandOutcome.Uncertain : CommandOutcome.Pending)
            || isRetry && operation.AuthorizedAttemptCount >= operation.RequestLimit)
        {
            return Skipped("the operation is not awaiting this request");
        }

        Attempt? previous = null;
        if (isRetry)
        {
            previous = await database.Attempts.AsNoTracking()
                .SingleAsync(
                    value => value.OperationId == operation.Id && value.Number == operation.AuthorizedAttemptCount,
                    cancellationToken);
            if (previous.FinishedAt is null)
            {
                return Skipped("the previous request has not finished");
            }
        }

        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        var reservation = session.Reservation ?? throw new InvalidOperationException("Reservation must be loaded.");
        if (session.Lifecycle != SessionLifecycle.Starting || reservation.DriverId != operation.DriverId)
        {
            return Skipped("the session is no longer starting");
        }

        // A retry repeats the saved payload; it never remaps equipment or providers.
        Guid providerId;
        string? integrationKey;
        ProviderEquipmentAddress address;
        if (previous is null)
        {
            var mapping = await LoadMappingAsync(session.ConnectorId, cancellationToken)
                ?? throw new InvalidOperationException("The connector has no provider mapping.");
            var chargingProvider = mapping.ProviderCharger?.ProviderStation?.ChargingProvider
                ?? throw new InvalidOperationException("The charging provider must be loaded.");
            providerId = chargingProvider.Id;
            integrationKey = chargingProvider.IntegrationKey;
            address = mapping.ToProviderAddress();
        }
        else
        {
            providerId = previous.ChargingProviderId;
            integrationKey = previous.IntegrationKey;
            address = new(previous.ProviderStationId, previous.ProviderChargerId, previous.ProviderConnectorId);
        }

        providers.ValidateIntegration(integrationKey);

        bool ownsClaim = !await database.Sessions.AnyAsync(
            value => value.ConnectorId == session.ConnectorId && value.Id != session.Id
                && (value.Lifecycle == SessionLifecycle.Starting || value.Lifecycle == SessionLifecycle.Active
                    || value.Lifecycle == SessionLifecycle.Stopping),
            cancellationToken);
        if (!ownsClaim)
        {
            return Skipped("another session holds the connector");
        }

        var authorizedAt = clock.GetUtcNow();
        var eligibility = reservation.ValidateChargingStart(authorizedAt);
        if (eligibility.IsFailed && isRetry)
        {
            // An earlier attempt may have had effect, so ineligibility ends retrying without rejecting the start.
            logger.LogInformation(
                "Retry {AttemptNumber} of start {OperationId} is no longer eligible: {Errors}",
                attemptNumber,
                operation.PublicId,
                string.Join(", ", eligibility.Errors));
            return null;
        }

        if (eligibility.IsFailed)
        {
            var rejection = operation.RejectStart();
            if (rejection.IsFailed)
            {
                throw new InvalidOperationException(
                    $"The locked start {operation.PublicId} cannot be rejected: {string.Join(", ", rejection.Errors)}");
            }

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Start {OperationId} was rejected before execution", operation.PublicId);
            return null;
        }

        var attempt = await CommandExecution.AuthorizeAttemptAsync(
            database,
            transaction,
            outbox,
            options.Value,
            operation,
            attemptNumber,
            new ProviderBinding(providerId, integrationKey!, address),
            () => new CheckChargingStart(operation.Id, Automatic: true),
            authorizedAt,
            logger,
            cancellationToken);
        return attempt;
    }

    private Task ApplyOutcomeAsync(
        Attempt authorized, Result<ProviderCommandResult> result, CancellationToken cancellationToken) =>
        CommandExecution.ApplyOutcomeAsync(
            database,
            clock,
            logger,
            authorized,
            result,
            (operation, evidence) => evidence switch
            {
                ProviderEvidence.Applied => operation.ConfirmStart(
                    authorized.ChargingProviderId,
                    result.Value.SessionId!.Value.ToString("D", CultureInfo.InvariantCulture),
                    result.Value.AppliedAt!.Value),
                ProviderEvidence.Rejected => operation.RejectStart(),
                _ => operation.MarkUncertain()
            },
            cancellationToken);

    private Task<ProviderConnector?> LoadMappingAsync(Guid connectorId, CancellationToken cancellationToken) =>
        database.ProviderConnectors
            .Include(value => value.ProviderCharger!)
            .ThenInclude(value => value.ProviderStation!)
            .ThenInclude(value => value.ChargingProvider)
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.ConnectorId == connectorId, cancellationToken);
}
