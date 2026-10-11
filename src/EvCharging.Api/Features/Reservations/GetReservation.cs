using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

internal sealed record ReservationResponse(
    Guid Id,
    Guid StationId,
    string StationName,
    string StationAddress,
    string TimeZoneId,
    Guid ConnectorId,
    string ChargerName,
    int EvseNumber,
    int ConnectorNumber,
    string ConnectorType,
    string PowerSupply,
    decimal RatedPowerKW,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    ReservationState State,
    DateTimeOffset CreatedAt);

internal sealed class GetReservationQueryHandler(PlatformDbContext database)
{
    public async Task<Result<ReservationResponse>> HandleAsync(
        Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = user.GetUserIdentity();
        if (identity is null)
        {
            return Result.Fail<ReservationResponse>(new ForbiddenError());
        }

        var reservation = await database.Reservations
            .AsNoTracking()
            .OwnedBy(identity)
            .WithEquipment()
            .SingleOrDefaultAsync(reservation => reservation.PublicId == id, cancellationToken);
        if (reservation is null)
        {
            if (await database.Reservations.AnyAsync(reservation => reservation.PublicId == id, cancellationToken))
            {
                return Result.Fail<ReservationResponse>(new ForbiddenError());
            }

            return Result.Fail<ReservationResponse>(new NotFoundError<Guid>("reservation", id));
        }

        return Result.Ok(reservation.ToResponse());
    }
}
