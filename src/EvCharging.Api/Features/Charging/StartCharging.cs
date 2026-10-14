using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Reservations;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Charging;

public sealed record StartChargingRequest(Guid IdempotencyKey);
public sealed record StartChargingCommand(Guid ReservationId, Guid IdempotencyKey)
    : IAuthenticatedRequest<StartOperationResponse>;

public sealed class StartChargingCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    TimeProvider clock)
{
    private const string IdempotencyMismatchCode = "idempotency_mismatch";
    private const string ConnectorBusyCode = "connector_busy";

    public async Task<Result<StartOperationResponse>> HandleAsync(
        StartChargingCommand command, UserIdentity identity, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey == Guid.Empty)
        {
            return Result.Fail<StartOperationResponse>(
                new ValidationError(nameof(StartChargingRequest.IdempotencyKey), "A request key is required."));
        }

        var driver = await database.Drivers.SingleOrDefaultAsync(
            value => value.IdentityIssuer == identity.Issuer && value.IdentitySubject == identity.Subject,
            cancellationToken);
        if (driver is null)
        {
            return Result.Fail<StartOperationResponse>(new ForbiddenError());
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driver.Id, cancellationToken);
        var existing = await FindOperationAsync(driver.Id, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var originalReservation = existing.Session?.Reservation
                ?? throw new InvalidOperationException("Reservation must be loaded.");
            return originalReservation.PublicId == command.ReservationId
                ? Result.Ok(existing.ToResponse())
                : Conflict(IdempotencyMismatchCode, "This request key was already used for a different reservation.");
        }

        var loaded = await LoadReservationAsync(command.ReservationId, identity, cancellationToken);
        if (loaded.IsFailed)
        {
            return Result.Fail<StartOperationResponse>(loaded.Errors);
        }

        var reservation = loaded.Value;
        var accepted = ChargingSession.AcceptStart(reservation, clock.GetUtcNow());
        if (accepted.IsFailed)
        {
            return Result.Fail<StartOperationResponse>(accepted.Errors);
        }

        if (await HasConnectorClaimAsync(reservation.ConnectorId, cancellationToken))
        {
            return Conflict(ConnectorBusyCode, "The connector is still in use or has an unresolved start.");
        }

        var operation = Operation.AcceptStart(accepted.Value, command.IdempotencyKey);
        return await SaveIntentAsync(operation, cancellationToken);
    }

    private Task<Operation?> FindOperationAsync(Guid driverId, Guid key, CancellationToken cancellationToken) =>
        database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleOrDefaultAsync(
                value => value.DriverId == driverId && value.Kind == ChargingCommandKind.Start
                    && value.IdempotencyKey == key,
                cancellationToken);

    private async Task<Result<Reservation>> LoadReservationAsync(
        Guid publicId, UserIdentity identity, CancellationToken cancellationToken)
    {
        var reservation = await database.Reservations.OwnedBy(identity)
            .SingleOrDefaultAsync(value => value.PublicId == publicId, cancellationToken);
        if (reservation is not null)
        {
            return Result.Ok(reservation);
        }

        return await database.Reservations.AnyAsync(value => value.PublicId == publicId, cancellationToken)
            ? Result.Fail<Reservation>(new ForbiddenError())
            : Result.Fail<Reservation>(new NotFoundError<Guid>("reservation", publicId));
    }

    private Task<bool> HasConnectorClaimAsync(Guid connectorId, CancellationToken cancellationToken) =>
        database.Sessions.AnyAsync(
            value => value.ConnectorId == connectorId
                && (value.Lifecycle == SessionLifecycle.Starting || value.Lifecycle == SessionLifecycle.Active
                    || value.Lifecycle == SessionLifecycle.Stopping),
            cancellationToken);

    private async Task<Result<StartOperationResponse>> SaveIntentAsync(
        Operation operation, CancellationToken cancellationToken)
    {
        database.Operations.Add(operation);
        outbox.Enroll(database);
        await outbox.PublishAsync(new StartCharging(operation.Id));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsChargingClaimConflict())
        {
            return Conflict(ConnectorBusyCode, "The reservation or connector already has a charging session.");
        }

        return Result.Ok(operation.ToResponse());
    }

    private static Result<StartOperationResponse> Conflict(string code, string message) =>
        Result.Fail<StartOperationResponse>(new ChargingStartError(code, message));
}
