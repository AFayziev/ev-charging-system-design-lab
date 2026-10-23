using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.ChargingWorker.Handlers;

/// <summary>Numbered-attempt mechanics shared by every kind of provider command.</summary>
internal static class CommandExecutionExtensions
{
    public const string TransportFailureCode = "provider_transport_failure";
    public const string InvalidResponseCode = "provider_response_unmatched";
    public const string UnrecordedResponseCode = "provider_response_unrecorded";

    public static Task<Operation?> LoadOperationAsync(
        this PlatformDbContext database, Guid id, CancellationToken cancellationToken) =>
        database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    /// <summary>
    /// Spends the operation's next request number and records the attempt, so redelivery and overlapping checks
    /// cannot spend extra budget. A retry commits here; the first request also schedules its automatic checks.
    /// </summary>
    public static async Task<Attempt?> AuthorizeAttemptAsync(
        this PlatformDbContext database,
        IDbContextTransaction transaction,
        IDbContextOutbox outbox,
        ChargingExecutionOptions policy,
        Operation operation,
        int attemptNumber,
        ProviderBinding binding,
        Func<object> createCheck,
        DateTimeOffset authorizedAt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        bool isRetry = attemptNumber > 1;
        bool claimed = await ClaimAttemptAsync(
            database, operation, attemptNumber, policy.MaximumCommandRequests, cancellationToken);
        if (!claimed)
        {
            logger.LogInformation(
                "{Kind} request {AttemptNumber} for operation {OperationId} was not authorized: "
                + "a concurrent delivery claimed the request",
                operation.Kind,
                attemptNumber,
                operation.Id);
            return null;
        }

        // The batch claim bypasses tracking. Reload before any subsequent tracked write.
        await database.Entry(operation).ReloadAsync(cancellationToken);
        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        var attempt = new Attempt
        {
            OperationId = operation.Id,
            Number = operation.AuthorizedAttemptCount,
            AuthorizedAt = authorizedAt,
            ChargingProviderId = binding.ProviderId,
            IntegrationKey = binding.IntegrationKey,
            ProviderStationId = binding.Address.StationId,
            ProviderChargerId = binding.Address.ChargerId,
            ProviderConnectorId = binding.Address.ConnectorId,
            OperationPublicId = operation.PublicId,
            SessionPublicId = session.PublicId
        };
        database.Attempts.Add(attempt);
        if (isRetry)
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else if (!await ScheduleAutomaticChecksAsync(
            database, outbox, operation, policy, authorizedAt, createCheck, logger, cancellationToken))
        {
            return null;
        }

        logger.LogInformation(
            "Authorized attempt {AttemptId} ({AttemptNumber}) for {Kind} {OperationId}",
            attempt.Id,
            attemptNumber,
            operation.Kind,
            operation.PublicId);
        return attempt;
    }

    /// <summary>
    /// Records the provider's response on the authorized attempt and applies the matching transition to its
    /// operation, unless a check already settled it.
    /// </summary>
    public static async Task ApplyOutcomeAsync(
        this PlatformDbContext database,
        TimeProvider clock,
        ILogger logger,
        Attempt authorized,
        Result<ProviderCommandResult> result,
        Func<Operation, ProviderEvidence, Result> transition,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var driverId = await database.Operations.Where(value => value.Id == authorized.OperationId)
            .Select(value => value.DriverId)
            .SingleAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId, cancellationToken);
        var attempt = await database.Attempts.SingleAsync(value => value.Id == authorized.Id, cancellationToken);
        var operation = await database.LoadOperationAsync(authorized.OperationId, cancellationToken)
            ?? throw new InvalidOperationException("The authorized operation is missing.");
        if (attempt.FinishedAt is not null || operation.Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected)
        {
            return;
        }

        var transitioned = transition(operation, RecordResult(attempt, result));
        if (transitioned.IsFailed)
        {
            throw new InvalidOperationException(
                $"The authorized {operation.Kind} {operation.PublicId} cannot accept its provider outcome: "
                + string.Join(", ", transitioned.Errors));
        }

        attempt.Outcome = operation.Outcome;
        attempt.FinishedAt = clock.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "{Kind} {OperationId} completed attempt {AttemptId} with {Outcome}",
            operation.Kind,
            operation.PublicId,
            attempt.Id,
            operation.Outcome);
    }

    /// <summary>
    /// Applies provider evidence found by a status check, unless the operation is already settled. An attempt whose
    /// response was never recorded is closed with the result.
    /// </summary>
    public static async Task ResolveAsync(
        this PlatformDbContext database,
        TimeProvider clock,
        ILogger logger,
        Operation unresolved,
        Func<Operation, Result> transition,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(unresolved.DriverId, cancellationToken);
        var operation = await database.LoadOperationAsync(unresolved.Id, cancellationToken)
            ?? throw new InvalidOperationException("The checked operation is missing.");
        if (operation.Outcome is not (CommandOutcome.Confirmed or CommandOutcome.Rejected))
        {
            var transitioned = transition(operation);
            if (transitioned.IsFailed)
            {
                throw new InvalidOperationException(
                    $"{operation.Kind} {operation.PublicId} cannot accept provider evidence: "
                    + string.Join(", ", transitioned.Errors));
            }

            var latest = await database.Attempts.Where(value => value.OperationId == operation.Id)
                .OrderByDescending(value => value.Number)
                .FirstAsync(cancellationToken);
            if (latest.FinishedAt is null)
            {
                latest.FinishedAt = clock.GetUtcNow();
                latest.FailureCode = UnrecordedResponseCode;
                latest.Outcome = operation.Outcome;
            }

            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Status check resolved {Kind} {OperationId} with {Outcome}",
            operation.Kind,
            operation.PublicId,
            operation.Outcome);
    }

    /// <summary>
    /// The provider has no record of the latest uncertain request, so the same operation key is sent again.
    /// A request refused for access establishes nothing about the earlier ones, so it ends resending.
    /// A request whose response was never recorded and that has no provider record is closed as uncertain, once it
    /// is older than a status check interval: a younger one may still be in flight.
    /// Authorization rechecks eligibility and the request limit; overlapping checks claim one attempt number.
    /// </summary>
    public static async Task ResendAsync(
        this PlatformDbContext database,
        TimeProvider clock,
        IMessageBus bus,
        ILogger logger,
        Operation operation,
        Attempt latest,
        TimeSpan unrecordedAfter,
        Func<int, object> createRequest,
        CancellationToken cancellationToken)
    {
        if (latest.Number >= operation.RequestLimit)
        {
            return;
        }

        bool resendable = latest.FinishedAt is null
            ? clock.GetUtcNow() - latest.AuthorizedAt >= unrecordedAfter
                && await CloseUnrecordedAsync(database, clock, logger, operation.Id, latest.Number, cancellationToken)
            : operation.Outcome == CommandOutcome.Uncertain
                && latest.FailureCode is TransportFailureCode or InvalidResponseCode or UnrecordedResponseCode;
        if (!resendable)
        {
            return;
        }

        await bus.PublishAsync(createRequest(latest.Number + 1));
        logger.LogInformation(
            "Status check found no provider record for {Kind} {OperationId}; resending as request {AttemptNumber}",
            operation.Kind,
            operation.PublicId,
            latest.Number + 1);
    }

    /// <summary>
    /// Closes the latest attempt as uncertain when it is still unfinished, so the next request can be authorized.
    /// A response recorded in the meantime, or any change to the operation, leaves it as it is.
    /// </summary>
    private static async Task<bool> CloseUnrecordedAsync(
        PlatformDbContext database,
        TimeProvider clock,
        ILogger logger,
        Guid operationId,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var driverId = await database.Operations.Where(value => value.Id == operationId)
            .Select(value => value.DriverId)
            .SingleAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId, cancellationToken);
        var operation = await database.LoadOperationAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("The checked operation is missing.");
        var attempt = await database.Attempts.SingleAsync(
            value => value.OperationId == operationId && value.Number == attemptNumber, cancellationToken);
        if (attempt.FinishedAt is not null || operation.AuthorizedAttemptCount != attemptNumber
            || operation.Outcome is not (CommandOutcome.Pending or CommandOutcome.Uncertain)
            || operation.MarkUncertain().IsFailed)
        {
            return false;
        }

        attempt.Outcome = operation.Outcome;
        attempt.FailureCode = UnrecordedResponseCode;
        attempt.FinishedAt = clock.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "{Kind} {OperationId} request {AttemptNumber} has no recorded response and is treated as uncertain",
            operation.Kind,
            operation.PublicId,
            attemptNumber);
        return true;
    }

    /// <summary>Runs an automatic check inside the operation's checking window.</summary>
    public static async Task<ChargingCheckResult> RunAutomaticCheckAsync(
        this PlatformDbContext database,
        TimeProvider clock,
        Guid operationId,
        Func<CancellationToken, Task<ChargingCheckResult>> check,
        CancellationToken cancellationToken)
    {
        var endsAt = await database.Operations.AsNoTracking()
            .Where(value => value.Id == operationId)
            .Select(value => value.AutomaticChecksEndAt)
            .SingleOrDefaultAsync(cancellationToken);
        var remaining = endsAt - clock.GetUtcNow();
        if (remaining is null || remaining <= TimeSpan.Zero)
        {
            return new(ChargingCheckOutcome.Unresolved);
        }

        using var window = new CancellationTokenSource(remaining.Value, clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, window.Token);
        try
        {
            return await check(bounded.Token);
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            // An expired checking window is not evidence about the external effect.
            return new(ChargingCheckOutcome.Unresolved);
        }
    }

    private static async Task<bool> ClaimAttemptAsync(
        PlatformDbContext database,
        Operation operation,
        int attemptNumber,
        int firstRequestLimit,
        CancellationToken cancellationToken)
    {
        bool isRetry = attemptNumber > 1;
        var kind = operation.Kind;
        var outcome = operation.Outcome;
        int claimed = await database.Operations
            .Where(value => value.Id == operation.Id && value.Kind == kind
                && value.Outcome == outcome && value.AuthorizedAttemptCount == attemptNumber - 1
                && (!isRetry || value.AuthorizedAttemptCount < value.RequestLimit))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.AuthorizedAttemptCount, attemptNumber)
                    .SetProperty(
                        value => value.RequestLimit, value => isRetry ? value.RequestLimit : firstRequestLimit),
                cancellationToken);
        return claimed == 1;
    }

    private static async Task<bool> ScheduleAutomaticChecksAsync(
        PlatformDbContext database,
        IDbContextOutbox outbox,
        Operation operation,
        ChargingExecutionOptions policy,
        DateTimeOffset authorizedAt,
        Func<object> createCheck,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // One extra interval lets the final scheduled check run before the window closes.
        var endsAt = authorizedAt + policy.AutomaticStatusCheckInterval * (policy.MaximumAutomaticStatusChecks + 1);
        var scheduling = operation.ScheduleAutomaticChecks(endsAt);
        if (scheduling.IsFailed)
        {
            logger.LogWarning(
                "Automatic checks cannot be scheduled for {Kind} {OperationId}: {Errors}",
                operation.Kind,
                operation.PublicId,
                string.Join(", ", scheduling.Errors));
            return false;
        }

        outbox.Enroll(database);
        for (int number = 1; number <= policy.MaximumAutomaticStatusChecks; number++)
        {
            await outbox.ScheduleAsync(
                createCheck(),
                authorizedAt + policy.AutomaticStatusCheckInterval * number,
                new DeliveryOptions { DeliverBy = endsAt });
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return true;
    }

    private static ProviderEvidence RecordResult(Attempt attempt, Result<ProviderCommandResult> result)
    {
        if (result.IsSuccess && result.Value.ExternalOperationId == attempt.OperationPublicId)
        {
            var evidence = result.Value;
            attempt.ProviderCommandId = evidence.Id;
            attempt.ProviderSessionId = evidence.SessionId;
            attempt.ProviderAppliedAt = evidence.AppliedAt;
            return ProviderEvidence.Applied;
        }

        if (result.HasError<ProviderRejectedError>(out var rejected)
            && rejected.First().ExternalOperationId == attempt.OperationPublicId)
        {
            var evidence = rejected.First();
            attempt.ProviderCommandId = evidence.CommandId;
            attempt.FailureCode = evidence.Code;
            attempt.ResponseStatusCode = (int?)evidence.StatusCode;
            attempt.ResponseBody = evidence.ResponseBody;
            // An access denial establishes non-execution of this request only, so it cannot settle a command
            // whose earlier attempts were uncertain.
            bool deniedRetry = evidence.CommandId is null && attempt.Number > 1;
            return deniedRetry ? ProviderEvidence.Uncertain : ProviderEvidence.Rejected;
        }

        attempt.FailureCode = result.HasError<ProviderTransportError>() ? TransportFailureCode : InvalidResponseCode;
        if (result.HasError<ProviderResponseError>(out var errors))
        {
            var evidence = errors.First();
            attempt.ResponseBody = evidence.ResponseBody;
            attempt.ResponseStatusCode = (int)evidence.StatusCode;
        }

        return ProviderEvidence.Uncertain;
    }
}
