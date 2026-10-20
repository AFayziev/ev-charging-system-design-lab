using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

internal static class ReservationQueryExtensions
{
    public static IQueryable<Reservation> OwnedBy(this IQueryable<Reservation> query, UserIdentity identity) =>
        query.Where(reservation => reservation.Driver != null
            && reservation.Driver.IdentityIssuer == identity.Issuer
            && reservation.Driver.IdentitySubject == identity.Subject);

    public static async Task<Result<Reservation>> LoadOwnedAsync(
        this PlatformDbContext database, Guid publicId, UserIdentity identity, CancellationToken cancellationToken)
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

    public static Task<Operation?> FindOperationAsync(
        this PlatformDbContext database,
        Guid driverId,
        ChargingCommandKind kind,
        Guid idempotencyKey,
        CancellationToken cancellationToken) =>
        database.Operations.Include(value => value.Session!).ThenInclude(value => value.Reservation)
            .SingleOrDefaultAsync(
                value => value.DriverId == driverId && value.Kind == kind && value.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public static IQueryable<Reservation> WithEquipment(this IQueryable<Reservation> query) =>
        query
            .Include(reservation => reservation.Connector)
            .ThenInclude(connector => connector!.Charger)
            .ThenInclude(charger => charger!.Station);
}
