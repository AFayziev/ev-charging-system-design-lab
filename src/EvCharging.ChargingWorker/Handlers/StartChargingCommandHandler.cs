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
using Wolverine;
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
    internal const string TransportFailureCode = "provider_transport_failure";
    internal const string InvalidResponseCode = "provider_response_unmatched";

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
        var operation = await LoadOperationAsync(operationId, cancellationToken);
        // A message authorizes only its own numbered attempt, so redelivery cannot spend extra budget.
        if (operation is null || operation.Kind != ChargingCommandKind.Start
            || operation.AuthorizedAttemptCount != attemptNumber - 1
            || operation.Outcome != (isRetry ? CommandOutcome.Uncertain : CommandOutcome.Pending)
            || isRetry && operation.AuthorizedAttemptCount >= operation.StartRequestLimit)
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

        int claimed = await database.Operations
            .Where(value => value.Id == operation.Id && value.Kind == ChargingCommandKind.Start
                && value.Outcome == operation.Outcome && value.AuthorizedAttemptCount == attemptNumber - 1
                && (!isRetry || value.AuthorizedAttemptCount < value.StartRequestLimit))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.AuthorizedAttemptCount, attemptNumber)
                    .SetProperty(
                        value => value.StartRequestLimit,
                        value => isRetry ? value.StartRequestLimit : options.Value.MaximumStartRequests),
                cancellationToken);
        if (claimed == 0)
        {
            return Skipped("a concurrent delivery claimed the request");
        }

        // The batch claim bypasses tracking. Reload before any subsequent tracked write.
        await database.Entry(operation).ReloadAsync(cancellationToken);
        var attempt = new Attempt
        {
            OperationId = operation.Id,
            Number = operation.AuthorizedAttemptCount,
            AuthorizedAt = authorizedAt,
            ChargingProviderId = providerId,
            IntegrationKey = integrationKey!,
            ProviderStationId = address.StationId,
            ProviderChargerId = address.ChargerId,
            ProviderConnectorId = address.ConnectorId,
            OperationPublicId = operation.PublicId,
            SessionPublicId = session.PublicId
        };
        database.Attempts.Add(attempt);
        if (isRetry)
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Authorized retry attempt {AttemptId} ({AttemptNumber}) for start {OperationId}",
                attempt.Id,
                attemptNumber,
                operation.PublicId);
            return attempt;
        }

        var policy = options.Value;
        // One extra interval lets the final scheduled check run before the window closes.
        var endsAt = authorizedAt + policy.AutomaticStatusCheckInterval * (policy.MaximumAutomaticStatusChecks + 1);
        var scheduling = operation.ScheduleAutomaticChecks(endsAt);
        if (scheduling.IsFailed)
        {
            logger.LogWarning(
                "Automatic checks cannot be scheduled for start {OperationId}: {Errors}",
                operation.PublicId,
                string.Join(", ", scheduling.Errors));
            return null;
        }

        outbox.Enroll(database);
        for (int number = 1; number <= policy.MaximumAutomaticStatusChecks; number++)
        {
            await outbox.ScheduleAsync(
                new CheckChargingStart(operation.Id, Automatic: true),
                authorizedAt + policy.AutomaticStatusCheckInterval * number,
                new DeliveryOptions { DeliverBy = endsAt });
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        logger.LogInformation("Authorized attempt {AttemptId} for start {OperationId}", attempt.Id, operation.PublicId);
        return attempt;
    }

    private async Task ApplyOutcomeAsync(
        Attempt authorized, Result<ProviderCommandResult> result, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var driverId = await database.Operations.Where(value => value.Id == authorized.OperationId)
            .Select(value => value.DriverId)
            .SingleAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId, cancellationToken);
        var attempt = await database.Attempts.SingleAsync(value => value.Id == authorized.Id, cancellationToken);
        var operation = await LoadOperationAsync(authorized.OperationId, cancellationToken)
            ?? throw new InvalidOperationException("The authorized operation is missing.");
        if (attempt.FinishedAt is not null || operation.Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected)
        {
            return;
        }

        Result transition;
        if (result.IsSuccess && result.Value.ExternalOperationId == attempt.OperationPublicId)
        {
            var evidence = result.Value;
            transition = operation.ConfirmStart(
                attempt.ChargingProviderId,
                evidence.SessionId!.Value.ToString("D", CultureInfo.InvariantCulture),
                evidence.AppliedAt!.Value);
            attempt.ProviderCommandId = evidence.Id;
            attempt.ProviderSessionId = evidence.SessionId;
            attempt.ProviderAppliedAt = evidence.AppliedAt;
        }
        else if (result.HasError<ProviderRejectedError>(out var rejected)
            && rejected.First().ExternalOperationId == attempt.OperationPublicId)
        {
            var evidence = rejected.First();
            // An access denial establishes non-execution of this request only, so it cannot settle a start
            // whose earlier attempts were ambiguous.
            bool deniedRetry = evidence.CommandId is null && attempt.Number > 1;
            transition = deniedRetry ? operation.MarkUncertain() : operation.RejectStart();
            attempt.ProviderCommandId = evidence.CommandId;
            attempt.FailureCode = evidence.Code;
            attempt.ResponseStatusCode = (int?)evidence.StatusCode;
            attempt.ResponseBody = evidence.ResponseBody;
        }
        else
        {
            transition = operation.MarkUncertain();
            attempt.FailureCode = result.HasError<ProviderTransportError>()
                ? TransportFailureCode
                : InvalidResponseCode;
            if (result.HasError<ProviderResponseError>(out var errors))
            {
                var evidence = errors.First();
                attempt.ResponseBody = evidence.ResponseBody;
                attempt.ResponseStatusCode = (int)evidence.StatusCode;
            }
        }

        if (transition.IsFailed)
        {
            throw new InvalidOperationException(
                $"The authorized start {operation.PublicId} cannot accept its provider outcome: "
                + string.Join(", ", transition.Errors));
        }

        attempt.Outcome = operation.Outcome;
        attempt.FinishedAt = clock.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Start {OperationId} completed attempt {AttemptId} with {Outcome}",
            operation.PublicId,
            attempt.Id,
            operation.Outcome);
    }

    private Task<Operation?> LoadOperationAsync(Guid id, CancellationToken cancellationToken) =>
        database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    private Task<ProviderConnector?> LoadMappingAsync(Guid connectorId, CancellationToken cancellationToken) =>
        database.ProviderConnectors
            .Include(value => value.ProviderCharger!)
            .ThenInclude(value => value.ProviderStation!)
            .ThenInclude(value => value.ChargingProvider)
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.ConnectorId == connectorId, cancellationToken);
}
