using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

public sealed record ReservationResponse(
    Guid Id,
    Guid StationId,
    string StationName,
    string StationAddress,
    string TimeZoneId,
    Guid ConnectorId,
    string ChargerName,
    int ConnectorNumber,
    string ConnectorType,
    string PowerSupply,
    decimal RatedPowerKW,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    ReservationState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset ArrivalDeadline,
    StartOperationResponse? StartOperation = null,
    StopOperationResponse? StopOperation = null);

public sealed record GetReservationRequest(Guid Id) : IAuthenticatedRequest<ReservationResponse>;

public sealed class GetReservationQueryHandler(PlatformDbContext database)
{
    public async Task<Result<ReservationResponse>> HandleAsync(
        GetReservationRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        var reservation = await database.Reservations
            .AsNoTracking()
            .OwnedBy(identity)
            .WithEquipment()
            .SingleOrDefaultAsync(reservation => reservation.PublicId == request.Id, cancellationToken);
        if (reservation is null)
        {
            bool exists = await database.Reservations.AnyAsync(
                reservation => reservation.PublicId == request.Id, cancellationToken);
            if (exists)
            {
                return Result.Fail<ReservationResponse>(new ForbiddenError());
            }

            return Result.Fail<ReservationResponse>(new NotFoundError<Guid>("reservation", request.Id));
        }

        var responses = await new[] { reservation }.ToResponsesAsync(database, cancellationToken);
        return Result.Ok(responses[0]);
    }
}
