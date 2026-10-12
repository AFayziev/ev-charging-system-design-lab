using System.Diagnostics;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

public sealed record CancelReservationCommand(Guid Id) : IAuthenticatedRequest<ReservationResponse>;

public sealed class CancelReservationCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    TimeProvider clock)
{
    public async Task<Result<ReservationResponse>> HandleAsync(
        CancelReservationCommand command, UserIdentity identity, CancellationToken cancellationToken)
    {
        var driverId = await database.Reservations
            .AsNoTracking()
            .OwnedBy(identity)
            .Where(reservation => reservation.PublicId == command.Id)
            .Select(reservation => (Guid?)reservation.DriverId)
            .SingleOrDefaultAsync(cancellationToken);
        if (driverId is null)
        {
            bool exists = await database.Reservations.AnyAsync(
                reservation => reservation.PublicId == command.Id, cancellationToken);
            return exists
                ? Result.Fail<ReservationResponse>(new ForbiddenError())
                : Result.Fail<ReservationResponse>(new NotFoundError<Guid>("reservation", command.Id));
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driverId.Value, cancellationToken);
        var reservation = await database.Reservations
            .OwnedBy(identity)
            .WithEquipment()
            .SingleOrDefaultAsync(reservation => reservation.PublicId == command.Id, cancellationToken);
        if (reservation is null)
        {
            return Result.Fail<ReservationResponse>(new NotFoundError<Guid>("reservation", command.Id));
        }

        Activity.Current?.SetTag("reservation.id", reservation.Id.ToString());
        Activity.Current?.SetTag("reservation.public_id", reservation.PublicId.ToString());

        if (reservation.State == ReservationState.Cancelled)
        {
            return Result.Ok(reservation.ToResponse());
        }

        var hasUnresolvedStart = await database.Sessions.AnyAsync(
            session => session.ReservationId == reservation.Id && session.Lifecycle == SessionLifecycle.Starting,
            cancellationToken);
        var cancellation = reservation.Cancel(clock.GetUtcNow(), hasUnresolvedStart);
        if (cancellation.IsFailed)
        {
            return Result.Fail<ReservationResponse>(cancellation.Errors);
        }

        outbox.Enroll(database);
        await outbox.PublishAsync(new ReservationCancelled(reservation.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return Result.Ok(reservation.ToResponse());
    }
}
