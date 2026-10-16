using System.ComponentModel.DataAnnotations;
using EvCharging.SynchronousChargingProviderApi.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SynchronousChargingProviderApi.Features.Commands;

public sealed record StartChargingRequest(
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string StationId,
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string ChargerId,
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string ConnectorId,
    Guid SessionReference);

internal static class ProviderErrorCodes
{
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string ConnectorUnavailable = "connector_unavailable";
    public const string SessionReferenceConflict = "session_reference_conflict";
}

internal sealed class IdempotencyConflictError()
    : Error("The idempotency key was used with a different start request.");

internal sealed class ChargingStartRejectedError(Guid commandId, Guid reference, string code)
    : Error(GetMessage(code))
{
    public Guid CommandId { get; } = commandId;
    public Guid Reference { get; } = reference;
    public string Code { get; } = code;

    private static string GetMessage(string code) => code switch
    {
        ProviderErrorCodes.ConnectorUnavailable => "The connector is unavailable for charging.",
        ProviderErrorCodes.SessionReferenceConflict => "The session reference is already used by another start.",
        _ => throw new InvalidOperationException("The charging rejection code is not recognized.")
    };
}

internal sealed class StartChargingCommandHandler(
    ProviderDbContext database,
    TimeProvider clock,
    ILogger<StartChargingCommandHandler> logger)
{
    public async Task<Result<CommandResponse>> HandleAsync(
        Guid reference,
        StartChargingRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ProviderCommand
        {
            Reference = reference,
            StationId = request.StationId,
            ChargerId = request.ChargerId,
            ConnectorId = request.ConnectorId,
            SessionReference = request.SessionReference,
            CreatedAt = clock.GetUtcNow()
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (!await database.TryClaimCommandAsync(command, cancellationToken))
        {
            return await ReadExistingAsync(reference, request, cancellationToken);
        }

        database.Commands.Attach(command);
        await ApplyStartAsync(command, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        // Return stored timestamp precision so the initial response and later replays match.
        await database.Entry(command).ReloadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = command.ToResponse();
        logger.LogInformation(
            "Provider command {ProviderCommandId} for operation {OperationReference} committed as {Outcome}; "
                + "session {SessionId}, rejection {RejectionCode}",
            response.Id,
            response.Reference,
            response.Outcome,
            response.SessionId,
            response.Rejection);
        return ToStartResult(response);
    }

    private async Task<Result<CommandResponse>> ReadExistingAsync(
        Guid reference,
        StartChargingRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await database.Commands.AsNoTracking().Include(command => command.Session)
            .SingleAsync(command => command.Reference == reference, cancellationToken);
        return existing.StationId == request.StationId && existing.ChargerId == request.ChargerId
            && existing.ConnectorId == request.ConnectorId && existing.SessionReference == request.SessionReference
                ? ToStartResult(existing.ToResponse()) : Result.Fail<CommandResponse>(new IdempotencyConflictError());
    }

    private static Result<CommandResponse> ToStartResult(CommandResponse response) =>
        response.Outcome == CommandOutcome.Applied
            ? Result.Ok(response)
            : Result.Fail<CommandResponse>(new ChargingStartRejectedError(
                response.Id,
                response.Reference,
                response.Rejection ?? throw new InvalidOperationException("A rejected command requires a reason.")));

    private async Task ApplyStartAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        if (!await database.TryClaimConnectorAsync(command, cancellationToken))
        {
            command.Reject(ProviderErrorCodes.ConnectorUnavailable, clock.GetUtcNow());
            return;
        }

        var connector = await database.Connectors.AsNoTracking().SingleAsync(
            connector => connector.StationId == command.StationId && connector.ChargerId == command.ChargerId
                && connector.ConnectorId == command.ConnectorId,
            cancellationToken);
        var session = new ProviderSession
        {
            Reference = command.SessionReference,
            StationId = command.StationId,
            ChargerId = command.ChargerId,
            ConnectorId = command.ConnectorId,
            StartedAt = clock.GetUtcNow(),
            PowerKw = connector.PowerKw
        };
        if (!await database.TryCreateSessionAsync(session, cancellationToken))
        {
            await database.ReleaseConnectorAsync(command.Id, cancellationToken);
            command.Reject(ProviderErrorCodes.SessionReferenceConflict, clock.GetUtcNow());
            return;
        }

        database.Sessions.Attach(session);
        command.Apply(session, session.StartedAt);
    }
}
