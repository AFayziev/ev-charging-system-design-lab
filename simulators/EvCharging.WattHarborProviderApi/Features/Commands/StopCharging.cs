using EvCharging.WattHarborProviderApi.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

public sealed record StopChargingRequest(Guid ExternalSessionId);

internal sealed class ChargingStopRejectedError(Guid commandId, Guid externalOperationId, string code)
    : Error("The provider has no session with this external session ID.")
{
    public Guid CommandId { get; } = commandId;
    public Guid ExternalOperationId { get; } = externalOperationId;
    public string Code { get; } = code;
}

internal sealed class StopChargingCommandHandler(
    ProviderDbContext database,
    TimeProvider clock,
    ILogger<StopChargingCommandHandler> logger)
{
    public async Task<Result<CommandResponse>> HandleAsync(
        Guid externalOperationId,
        StopChargingRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ProviderCommand
        {
            Kind = CommandKind.Stop,
            ExternalOperationId = externalOperationId,
            ExternalSessionId = request.ExternalSessionId,
            CreatedAt = clock.GetUtcNow()
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (!await database.TryClaimCommandAsync(command, cancellationToken))
        {
            return await ReadExistingAsync(externalOperationId, request, cancellationToken);
        }

        database.Commands.Attach(command);
        await ApplyStopAsync(command, cancellationToken);
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
        return ToStopResult(response);
    }

    private async Task<Result<CommandResponse>> ReadExistingAsync(
        Guid externalOperationId,
        StopChargingRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await database.Commands.AsNoTracking().Include(command => command.Session)
            .SingleAsync(command => command.ExternalOperationId == externalOperationId, cancellationToken);
        return existing.Kind == CommandKind.Stop && existing.ExternalSessionId == request.ExternalSessionId
            ? ToStopResult(existing.ToResponse()) : Result.Fail<CommandResponse>(new IdempotencyConflictError());
    }

    private static Result<CommandResponse> ToStopResult(CommandResponse response) =>
        response.Outcome == CommandOutcome.Applied
            ? Result.Ok(response)
            : Result.Fail<CommandResponse>(new ChargingStopRejectedError(
                response.Id,
                response.ExternalOperationId,
                response.Rejection ?? throw new InvalidOperationException("A rejected command requires a reason.")));

    private async Task ApplyStopAsync(ProviderCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = await database.Sessions.SingleOrDefaultAsync(
            session => session.ExternalSessionId == command.ExternalSessionId, cancellationToken);
        if (session is null)
        {
            command.Reject(ProviderErrorCodes.SessionNotFound, now);
            return;
        }

        // Stopping an ended session is applied: the requested end state already holds.
        if (await database.TryEndSessionAsync(session.Id, now, cancellationToken))
        {
            await database.ReleaseConnectorAsync(session, cancellationToken);
        }

        command.Apply(session, now);
    }
}
