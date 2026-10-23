using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Reservations;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Charging;

public sealed record StopChargingRequest(Guid IdempotencyKey);
public sealed record StopChargingCommand(Guid ReservationId, Guid IdempotencyKey)
    : IAuthenticatedRequest<StopOperationResponse>;

public sealed class StopChargingCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    TimeProvider clock)
{
    private const string IdempotencyMismatchCode = "idempotency_mismatch";

    public async Task<Result<StopOperationResponse>> HandleAsync(
        StopChargingCommand command, UserIdentity identity, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey == Guid.Empty)
        {
            return Result.Fail<StopOperationResponse>(
                new ValidationError(nameof(StopChargingRequest.IdempotencyKey), "A request key is required."));
        }

        var driver = await database.Drivers.SingleOrDefaultAsync(
            value => value.IdentityIssuer == identity.Issuer && value.IdentitySubject == identity.Subject,
            cancellationToken);
        if (driver is null)
        {
            return Result.Fail<StopOperationResponse>(new ForbiddenError());
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driver.Id, cancellationToken);
        var existing = await database.FindOperationAsync(
            driver.Id, ChargingCommandKind.Stop, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var originalReservation = existing.Session?.Reservation
                ?? throw new InvalidOperationException("Reservation must be loaded.");
            return originalReservation.PublicId == command.ReservationId
                ? Result.Ok(existing.ToStopResponse())
                : Conflict(IdempotencyMismatchCode, "This request key was already used for a different reservation.");
        }

        var loaded = await database.LoadOwnedAsync(command.ReservationId, identity, cancellationToken);
        if (loaded.IsFailed)
        {
            return Result.Fail<StopOperationResponse>(loaded.Errors);
        }

        var session = await database.Sessions.SingleOrDefaultAsync(
            value => value.ReservationId == loaded.Value.Id && value.Lifecycle != SessionLifecycle.Failed,
            cancellationToken);
        if (session is null)
        {
            return Conflict(
                ChargingStopError.SessionNotActiveCode, "This reservation has no charging session to stop.");
        }

        var accepted = Operation.AcceptStop(session, command.IdempotencyKey, clock.GetUtcNow());
        if (accepted.IsFailed)
        {
            return Result.Fail<StopOperationResponse>(accepted.Errors);
        }

        database.Operations.Add(accepted.Value);
        outbox.Enroll(database);
        await outbox.PublishAsync(new StopChargingMessage(accepted.Value.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return Result.Ok(accepted.Value.ToStopResponse());
    }

    private static Result<StopOperationResponse> Conflict(string code, string message) =>
        Result.Fail<StopOperationResponse>(new ChargingStopError(code, message));
}
