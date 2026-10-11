using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Persistence;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EvCharging.Api.Features.Reservations;

internal sealed record CreateReservationRequest(Guid ConnectorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

internal sealed class ReservationOverlapError() : Error("This connector is already reserved during the selected slot.");

internal sealed class CreateReservationCommandHandler(
    [FromKeyedServices(PlatformPersistenceExtensions.WriteRoute)] PlatformDbContext database,
    TimeProvider clock)
{
    public async Task<Result<ReservationResponse>> HandleAsync(
        CreateReservationRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = user.GetUserIdentity();
        if (identity is null)
        {
            return Result.Fail<ReservationResponse>(new ForbiddenError());
        }

        var driver = await database.Drivers.SingleOrDefaultAsync(
            driver => driver.IdentityIssuer == identity.Issuer && driver.IdentitySubject == identity.Subject,
            cancellationToken);
        if (driver is null)
        {
            return Result.Fail<ReservationResponse>(new ForbiddenError());
        }

        var connector = await database.Connectors
            .Include(connector => connector.Evse)
            .ThenInclude(evse => evse!.Charger)
            .ThenInclude(charger => charger!.Station)
            .SingleOrDefaultAsync(connector => connector.PublicId == request.ConnectorId, cancellationToken);
        if (connector is null)
        {
            return Result.Fail<ReservationResponse>(new NotFoundError<Guid>("connector", request.ConnectorId));
        }

        var booking = Reservation.Book(
            connector, driver, request.StartsAt, request.EndsAt, clock.GetUtcNow());
        if (booking.IsFailed)
        {
            return Result.Fail<ReservationResponse>(booking.Errors
                .Cast<BookingValidationError>()
                .Select(error => error.ToValidationError()));
        }

        var reservation = booking.Value;

        database.Reservations.Add(reservation);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: "ex_reservations_connector_slot" })
        {
            return Result.Fail<ReservationResponse>(new ReservationOverlapError());
        }

        return Result.Ok(reservation.ToResponse());
    }
}

internal static class CreateReservationMappings
{
    public static ValidationError ToValidationError(this BookingValidationError error) => new(
        error.Boundary switch
        {
            BookingBoundary.Start => nameof(CreateReservationRequest.StartsAt),
            BookingBoundary.End => nameof(CreateReservationRequest.EndsAt),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Boundary, "Unknown booking boundary.")
        },
        error.Message);
}
