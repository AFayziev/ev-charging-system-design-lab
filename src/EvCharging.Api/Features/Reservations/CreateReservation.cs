using System.Diagnostics;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

public sealed record CreateReservationRequest(Guid ConnectorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt)
    : IAuthenticatedRequest<ReservationResponse>;

internal sealed class ReservationAdmissionError(string message) : Error(message);

public sealed class CreateReservationCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    TimeProvider clock,
    IOptionsSnapshot<ReservationPolicyOptions> options)
{
    public async Task<Result<ReservationResponse>> HandleAsync(
        CreateReservationRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        var driver = await GetDriverAsync(identity, cancellationToken);
        if (driver is null)
        {
            return Result.Fail<ReservationResponse>(new ForbiddenError());
        }

        var connector = await GetConnectorAsync(request.ConnectorId, cancellationToken);
        if (connector is null)
        {
            return Result.Fail<ReservationResponse>(new NotFoundError<Guid>("connector", request.ConnectorId));
        }

        var limits = options.Value.ToDomain();
        var now = clock.GetUtcNow();
        var booking = Reservation.Book(connector, driver, request.StartsAt, request.EndsAt, now, limits);
        if (booking.IsFailed)
        {
            return Result.Fail<ReservationResponse>(booking.Errors
                .Cast<BookingValidationError>()
                .Select(error => error.ToValidationError()));
        }

        var reservation = booking.Value;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(driver.Id, cancellationToken);
        var admission = await CheckAdmissionAsync(reservation, limits, cancellationToken);
        if (admission.IsFailed)
        {
            return Result.Fail<ReservationResponse>(admission.Errors);
        }

        return await SaveReservationAsync(reservation, cancellationToken);
    }

    private Task<Driver?> GetDriverAsync(UserIdentity identity, CancellationToken cancellationToken) =>
        database.Drivers.SingleOrDefaultAsync(
            driver => driver.IdentityIssuer == identity.Issuer && driver.IdentitySubject == identity.Subject,
            cancellationToken);

    private Task<Connector?> GetConnectorAsync(Guid publicId, CancellationToken cancellationToken) =>
        database.Connectors
            .Include(connector => connector.Evse)
            .ThenInclude(evse => evse!.Charger)
            .ThenInclude(charger => charger!.Station)
            .SingleOrDefaultAsync(connector => connector.PublicId == publicId, cancellationToken);

    private async Task<Result> CheckAdmissionAsync(
        Reservation reservation, ReservationBookingLimits limits, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var startValidation = reservation.ValidateStartTime(now);
        if (startValidation.IsFailed)
        {
            return Result.Fail(startValidation.Errors
                .Cast<BookingValidationError>()
                .Select(error => error.ToValidationError()));
        }

        var outstandingCount = await ExpireOverdueReservationsAsync(reservation.DriverId, now, cancellationToken);
        var overlapping = await database.Reservations.AnyAsync(
            existing => existing.DriverId == reservation.DriverId
                && (existing.State == ReservationState.Active || existing.State == ReservationState.Consumed)
                && existing.StartsAt < reservation.EndsAt && reservation.StartsAt < existing.EndsAt,
            cancellationToken);
        if (overlapping)
        {
            return Result.Fail(new ReservationAdmissionError(
                "You already have a reservation during the selected slot."));
        }

        if (outstandingCount >= limits.MaximumOutstandingReservationsPerDriver)
        {
            return Result.Fail(new ReservationAdmissionError(
                $"You can have at most {limits.MaximumOutstandingReservationsPerDriver} outstanding reservations."));
        }

        return Result.Ok();
    }

    private async Task<int> ExpireOverdueReservationsAsync(
        Guid driverId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outstanding = await database.Reservations
            .Where(existing => existing.DriverId == driverId && existing.State == ReservationState.Active)
            .ToListAsync(cancellationToken);
        await database.ExpireReservationsAsync(outstanding, now, cancellationToken);

        return outstanding.Count(existing => existing.State == ReservationState.Active);
    }

    private async Task<Result<ReservationResponse>> SaveReservationAsync(
        Reservation reservation, CancellationToken cancellationToken)
    {
        database.Reservations.Add(reservation);
        Activity.Current?.SetTag("reservation.id", reservation.Id.ToString());
        outbox.Enroll(database);
        await outbox.PublishAsync(new ReservationCreated(reservation.Id, reservation.EndsAt));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsReservationOverlap())
        {
            return Result.Fail<ReservationResponse>(new ReservationAdmissionError(
                "This connector is already reserved during the selected slot."));
        }
        catch (DbUpdateException exception) when (exception.IsDriverReservationOverlap())
        {
            return Result.Fail<ReservationResponse>(new ReservationAdmissionError(
                "You already have a reservation during the selected slot."));
        }

        Activity.Current?.SetTag("reservation.public_id", reservation.PublicId.ToString());
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
