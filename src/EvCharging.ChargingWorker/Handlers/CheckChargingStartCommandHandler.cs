using System.Globalization;
using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class CheckChargingStartCommandHandler(
    PlatformDbContext database,
    ChargingProviderClientFactory providers,
    TimeProvider clock,
    IOptions<ChargingExecutionOptions> options,
    IMessageBus bus,
    ILogger<CheckChargingStartCommandHandler> logger)
{
    [NonTransactional]
    public async Task<ChargingCheckResult> HandleAsync(
        CheckChargingStart message,
        CancellationToken cancellationToken)
    {
        if (!message.Automatic)
        {
            return await CheckAsync(message.OperationId, resend: false, cancellationToken);
        }

        return await CommandExecution.RunAutomaticCheckAsync(
            database,
            clock,
            message.OperationId,
            token => CheckAsync(message.OperationId, resend: true, token),
            cancellationToken);
    }

    private async Task<ChargingCheckResult> CheckAsync(
        Guid operationId,
        bool resend,
        CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken);
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

        var evidence = await ReadEvidenceAsync(attempt, cancellationToken);
        if (evidence.Outcome != ChargingCheckOutcome.Resolved)
        {
            logger.LogInformation(
                "Status check for start {OperationId} returned {CheckOutcome}",
                operation.PublicId,
                evidence.Outcome);
            if (resend && evidence.Outcome == ChargingCheckOutcome.Unresolved)
            {
                await CommandExecution.ResendAsync(
                    database,
                    clock,
                    bus,
                    logger,
                    operation,
                    attempt,
                    options.Value.AutomaticStatusCheckInterval,
                    number => new StartCharging(operation.Id, number),
                    cancellationToken);
            }

            return new(evidence.Outcome);
        }

        await CommandExecution.ResolveAsync(
            database,
            clock,
            logger,
            operation,
            unresolved => evidence.Command is { Outcome: ProviderCommandOutcome.Rejected }
                ? unresolved.RejectStart()
                : unresolved.ConfirmStart(
                    attempt.ChargingProviderId,
                    (evidence.Command?.SessionId ?? evidence.Session!.Id).ToString("D", CultureInfo.InvariantCulture),
                    evidence.Command?.AppliedAt ?? evidence.Session!.StartedAt),
            cancellationToken);
        return new(ChargingCheckOutcome.Resolved);
    }

    private async Task<Evidence> ReadEvidenceAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        using var provider = providers.Create(attempt.IntegrationKey);
        var command = await provider.GetCommandByExternalOperationIdAsync(attempt.OperationPublicId, cancellationToken);
        if (command.IsFailed)
        {
            return new(ChargingCheckOutcome.Unavailable);
        }

        if (command.Value is { } found)
        {
            return found.ExternalOperationId == attempt.OperationPublicId && found.Kind == ProviderCommandKind.Start
                ? new(ChargingCheckOutcome.Resolved, Command: found)
                : new(ChargingCheckOutcome.Unavailable);
        }

        var session = await provider.GetSessionByExternalSessionIdAsync(attempt.SessionPublicId, cancellationToken);
        if (session.IsFailed)
        {
            return new(ChargingCheckOutcome.Unavailable);
        }

        if (session.Value is null)
        {
            return new(ChargingCheckOutcome.Unresolved);
        }

        var equipment = new ProviderEquipmentAddress(
            attempt.ProviderStationId, attempt.ProviderChargerId, attempt.ProviderConnectorId);
        return session.Value.ExternalSessionId == attempt.SessionPublicId && session.Value.Equipment == equipment
            ? new(ChargingCheckOutcome.Resolved, Session: session.Value)
            : new(ChargingCheckOutcome.Unavailable);
    }

    private sealed record Evidence(
        ChargingCheckOutcome Outcome,
        ProviderCommandResult? Command = null,
        ProviderSessionResult? Session = null);
}
