using System.Globalization;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class CheckChargingStartCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IMessageBus bus,
    ILogger<CheckChargingStartCommandHandler> logger)
{
    [NonTransactional]
    public async Task<ChargingStartCheckResult> HandleAsync(
        CheckChargingStart message,
        CancellationToken cancellationToken)
    {
        if (!message.Automatic)
        {
            return await CheckAsync(message.OperationId, resend: false, cancellationToken);
        }

        var endsAt = await database.Operations.AsNoTracking()
            .Where(value => value.Id == message.OperationId)
            .Select(value => value.AutomaticChecksEndAt)
            .SingleOrDefaultAsync(cancellationToken);
        var remaining = endsAt - clock.GetUtcNow();
        if (remaining is null || remaining <= TimeSpan.Zero)
        {
            return new(ChargingStartCheckOutcome.Unresolved);
        }

        using var window = new CancellationTokenSource(remaining.Value, clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, window.Token);
        try
        {
            return await CheckAsync(message.OperationId, resend: true, bounded.Token);
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            // An expired checking window is not evidence about the external effect.
            return new(ChargingStartCheckOutcome.Unresolved);
        }
    }

    private async Task<ChargingStartCheckResult> CheckAsync(
        Guid operationId,
        bool resend,
        CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return new(ChargingStartCheckOutcome.Unresolved);
        }

        if (operation.Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected)
        {
            return new(ChargingStartCheckOutcome.Resolved);
        }

        var attempt = await database.Attempts.AsNoTracking().Where(value => value.OperationId == operation.Id)
            .OrderByDescending(value => value.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            return new(ChargingStartCheckOutcome.Unresolved);
        }

        var evidence = await ReadEvidenceAsync(attempt, cancellationToken);
        if (evidence.Outcome != ChargingStartCheckOutcome.Resolved)
        {
            logger.LogInformation(
                "Status check for start {OperationId} returned {CheckOutcome}",
                operation.PublicId,
                evidence.Outcome);
            if (resend && evidence.Outcome == ChargingStartCheckOutcome.Unresolved)
            {
                await ResendAsync(operation, attempt);
            }

            return new(evidence.Outcome);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(operation.DriverId, cancellationToken);
        operation = await database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operationId, cancellationToken);
        if (operation.Outcome is not (CommandOutcome.Confirmed or CommandOutcome.Rejected))
        {
            var transition = evidence.Command is { Outcome: ProviderCommandOutcome.Rejected }
                ? operation.RejectStart()
                : operation.ConfirmStart(
                    attempt.ChargingProviderId,
                    (evidence.Command?.SessionId ?? evidence.Session!.Id).ToString("D", CultureInfo.InvariantCulture),
                    evidence.Command?.AppliedAt ?? evidence.Session!.StartedAt);
            if (transition.IsFailed)
            {
                throw new InvalidOperationException(
                    $"Start {operation.PublicId} cannot accept provider evidence: {string.Join(", ", transition.Errors)}");
            }

            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Status check resolved start {OperationId} with {Outcome}",
            operation.PublicId,
            operation.Outcome);
        return new(ChargingStartCheckOutcome.Resolved);
    }

    // The provider has no record of the latest ambiguous request, so the same operation key is sent again.
    // Authorization rechecks eligibility and the request limit; overlapping checks claim one attempt number.
    private async Task ResendAsync(Operation operation, Attempt latest)
    {
        // A request refused for access establishes nothing about the earlier ones, so it ends resending.
        if (operation.Outcome != CommandOutcome.Uncertain || latest.FinishedAt is null
            || latest.FailureCode is not (StartChargingCommandHandler.TransportFailureCode
                or StartChargingCommandHandler.InvalidResponseCode)
            || latest.Number >= operation.StartRequestLimit)
        {
            return;
        }

        await bus.PublishAsync(new StartCharging(operation.Id, latest.Number + 1));
        logger.LogInformation(
            "Status check found no provider record for start {OperationId}; resending as request {AttemptNumber}",
            operation.PublicId,
            latest.Number + 1);
    }

    private async Task<Evidence> ReadEvidenceAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        using var provider = providers.Create(attempt.IntegrationKey);
        var command = await provider.GetCommandByExternalOperationIdAsync(attempt.OperationPublicId, cancellationToken);
        if (command.IsFailed)
        {
            return new(ChargingStartCheckOutcome.Unavailable);
        }

        if (command.Value is { } found)
        {
            return found.ExternalOperationId == attempt.OperationPublicId
                ? new(ChargingStartCheckOutcome.Resolved, Command: found)
                : new(ChargingStartCheckOutcome.Unavailable);
        }

        var session = await provider.GetSessionByExternalSessionIdAsync(attempt.SessionPublicId, cancellationToken);
        if (session.IsFailed)
        {
            return new(ChargingStartCheckOutcome.Unavailable);
        }

        if (session.Value is null)
        {
            return new(ChargingStartCheckOutcome.Unresolved);
        }

        var equipment = new ProviderEquipmentAddress(
            attempt.ProviderStationId, attempt.ProviderChargerId, attempt.ProviderConnectorId);
        return session.Value.ExternalSessionId == attempt.SessionPublicId && session.Value.Equipment == equipment
            ? new(ChargingStartCheckOutcome.Resolved, Session: session.Value)
            : new(ChargingStartCheckOutcome.Unavailable);
    }

    private sealed record Evidence(
        ChargingStartCheckOutcome Outcome,
        ProviderCommandResult? Command = null,
        ProviderSessionResult? Session = null);
}
