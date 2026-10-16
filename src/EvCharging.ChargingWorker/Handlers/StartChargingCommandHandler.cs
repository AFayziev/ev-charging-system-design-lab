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

namespace EvCharging.ChargingWorker.Handlers;

public sealed class StartChargingCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IOptions<ChargingExecutionOptions> options,
    ILogger<StartChargingCommandHandler> logger)
{
    private const string TransportFailureCode = "provider_transport_failure";
    private const string InvalidResponseCode = "provider_response_unmatched";

    [NonTransactional]
    public async Task HandleAsync(StartCharging message, CancellationToken cancellationToken)
    {
        var attempt = await AuthorizeAsync(message.OperationId, cancellationToken);
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

    private async Task<Attempt?> AuthorizeAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var driverId = await database.Operations.Where(value => value.Id == operationId)
            .Select(value => (Guid?)value.DriverId)
            .SingleOrDefaultAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId.Value, cancellationToken);
        var operation = await LoadOperationAsync(operationId, cancellationToken);
        if (operation is null || operation.Kind != ChargingCommandKind.Start
            || operation.Outcome != CommandOutcome.Pending || operation.AuthorizedAttemptCount != 0)
        {
            return null;
        }

        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        var reservation = session.Reservation ?? throw new InvalidOperationException("Reservation must be loaded.");
        if (session.Lifecycle != SessionLifecycle.Starting || reservation.DriverId != operation.DriverId)
        {
            return null;
        }

        var mapping = await LoadMappingAsync(session.ConnectorId, cancellationToken);
        if (mapping is null)
        {
            throw new InvalidOperationException("The connector has no provider mapping.");
        }

        var chargingProvider = mapping.ProviderCharger?.ProviderStation?.ChargingProvider
            ?? throw new InvalidOperationException("The charging provider must be loaded.");
        providers.ValidateIntegration(chargingProvider.IntegrationKey);

        bool ownsClaim = !await database.Sessions.AnyAsync(
            value => value.ConnectorId == session.ConnectorId && value.Id != session.Id
                && (value.Lifecycle == SessionLifecycle.Starting || value.Lifecycle == SessionLifecycle.Active
                    || value.Lifecycle == SessionLifecycle.Stopping),
            cancellationToken);
        if (!ownsClaim)
        {
            return null;
        }

        var authorizedAt = clock.GetUtcNow();
        var eligibility = reservation.ValidateChargingStart(authorizedAt);
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
                && value.Outcome == CommandOutcome.Pending && value.AuthorizedAttemptCount == 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.AuthorizedAttemptCount, 1)
                    .SetProperty(
                        value => value.EffectAttemptLimit,
                        options.Value.MaximumEffectAttempts),
                cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        // The batch claim bypasses tracking. Reload before any subsequent tracked write.
        await database.Entry(operation).ReloadAsync(cancellationToken);
        var address = mapping.ToProviderAddress();
        var attempt = new Attempt
        {
            OperationId = operation.Id,
            Number = operation.AuthorizedAttemptCount,
            AuthorizedAt = authorizedAt,
            ChargingProviderId = chargingProvider.Id,
            IntegrationKey = chargingProvider.IntegrationKey!,
            ProviderStationId = address.StationId,
            ProviderChargerId = address.ChargerId,
            ProviderConnectorId = address.ConnectorId,
            OperationPublicId = operation.PublicId,
            SessionPublicId = session.PublicId
        };
        database.Attempts.Add(attempt);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
            transition = operation.RejectStart();
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
