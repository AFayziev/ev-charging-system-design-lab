using System.ComponentModel.DataAnnotations;
using EvCharging.WattHarborProviderApi.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

public sealed record StartChargingRequest(
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string StationId,
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string ChargerId,
    [property: Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string ConnectorId,
    Guid ExternalSessionId);

internal static class ProviderErrorCodes
{
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string ConnectorUnavailable = "connector_unavailable";
    public const string ExternalSessionIdConflict = "external_session_id_conflict";
}

internal sealed class IdempotencyConflictError()
    : Error("The idempotency key was used with a different start request.");

internal sealed class ChargingStartRejectedError(Guid commandId, Guid externalOperationId, string code)
    : Error(GetMessage(code))
{
    public Guid CommandId { get; } = commandId;
    public Guid ExternalOperationId { get; } = externalOperationId;
    public string Code { get; } = code;

    private static string GetMessage(string code) => code switch
    {
        ProviderErrorCodes.ConnectorUnavailable => "The connector is unavailable for charging.",
        ProviderErrorCodes.ExternalSessionIdConflict => "The external session ID is already used by another start.",
        _ => throw new InvalidOperationException("The charging rejection code is not recognized.")
    };
}

internal sealed class StartChargingCommandHandler(
    ProviderDbContext database,
    TimeProvider clock,
    ILogger<StartChargingCommandHandler> logger)
{
    public async Task<Result<CommandResponse>> HandleAsync(
        Guid externalOperationId,
        StartChargingRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ProviderCommand
        {
            ExternalOperationId = externalOperationId,
            StationId = request.StationId,
            ChargerId = request.ChargerId,
            ConnectorId = request.ConnectorId,
            ExternalSessionId = request.ExternalSessionId,
            CreatedAt = clock.GetUtcNow()
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (!await database.TryClaimCommandAsync(command, cancellationToken))
        {
            return await ReadExistingAsync(externalOperationId, request, cancellationToken);
        }

        database.Commands.Attach(command);
        await ApplyStartAsync(command, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        // Return stored timestamp precision so the initial response and later replays match.
        await database.Entry(command).ReloadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = command.ToResponse();
        logger.LogInformation(
            "Provider command {ProviderCommandId} for operation {ExternalOperationId} committed as {Outcome}; "
                + "session {SessionId}, rejection {RejectionCode}",
            response.Id,
            response.ExternalOperationId,
            response.Outcome,
            response.SessionId,
            response.Rejection);
        return ToStartResult(response);
    }

    private async Task<Result<CommandResponse>> ReadExistingAsync(
        Guid externalOperationId,
        StartChargingRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await database.Commands.AsNoTracking().Include(command => command.Session)
            .SingleAsync(command => command.ExternalOperationId == externalOperationId, cancellationToken);
        return existing.StationId == request.StationId && existing.ChargerId == request.ChargerId
            && existing.ConnectorId == request.ConnectorId && existing.ExternalSessionId == request.ExternalSessionId
                ? ToStartResult(existing.ToResponse()) : Result.Fail<CommandResponse>(new IdempotencyConflictError());
    }

    private static Result<CommandResponse> ToStartResult(CommandResponse response) =>
        response.Outcome == CommandOutcome.Applied
            ? Result.Ok(response)
            : Result.Fail<CommandResponse>(new ChargingStartRejectedError(
                response.Id,
                response.ExternalOperationId,
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
            ExternalSessionId = command.ExternalSessionId,
            StationId = command.StationId,
            ChargerId = command.ChargerId,
            ConnectorId = command.ConnectorId,
            StartedAt = clock.GetUtcNow(),
            PowerKw = connector.PowerKw
        };
        if (!await database.TryCreateSessionAsync(session, cancellationToken))
        {
            await database.ReleaseConnectorAsync(command.Id, cancellationToken);
            command.Reject(ProviderErrorCodes.ExternalSessionIdConflict, clock.GetUtcNow());
            return;
        }

        database.Sessions.Attach(session);
        command.Apply(session, session.StartedAt);
    }
}
