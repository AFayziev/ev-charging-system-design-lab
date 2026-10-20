using EvCharging.Domain.Auditing;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using FluentResults;

namespace EvCharging.Domain.Reservations;

public sealed class Reservation : IAuditableEntity
{
    private Reservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid ConnectorId { get; private set; }
    public Guid DriverId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset ArrivalDeadline { get; private set; }
    public ReservationState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public Connector? Connector { get; private set; }
    public Driver? Driver { get; private set; }
    public BookingInterval Slot => BookingInterval.Create(StartsAt, EndsAt).Value;

    public static Result<Reservation> Book(
        Connector connector,
        Driver driver,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        ReservationBookingLimits limits)
    {
        var slot = BookingInterval.Create(start, end);
        if (slot.IsFailed)
        {
            return Result.Fail<Reservation>(slot.Errors);
        }

        var reservation = new Reservation
        {
            Connector = connector,
            ConnectorId = connector.Id,
            Driver = driver,
            DriverId = driver.Id,
            StartsAt = slot.Value.Start,
            EndsAt = slot.Value.End,
            ArrivalDeadline = slot.Value.Start + (limits.ArrivalAllowance < slot.Value.Duration
                ? limits.ArrivalAllowance : slot.Value.Duration),
            State = ReservationState.Active,
            CreatedAt = now.ToUniversalTime()
        };
        var startValidation = reservation.ValidateStartTime(now);
        if (startValidation.IsFailed)
        {
            return Result.Fail<Reservation>(startValidation.Errors);
        }

        if (slot.Value.Start - now > limits.MaximumAdvanceBookingHorizon)
        {
            return Result.Fail<Reservation>(new BookingValidationError(
                BookingBoundary.Start, "The start exceeds the advance-booking limit."));
        }

        if (slot.Value.Duration > limits.MaximumBookingDuration)
        {
            return Result.Fail<Reservation>(new BookingValidationError(
                BookingBoundary.End, "The slot exceeds the maximum booking duration."));
        }

        return Result.Ok(reservation);
    }

    public Result ValidateStartTime(DateTimeOffset now) => StartsAt < now
        ? Result.Fail(new BookingValidationError(BookingBoundary.Start, "The start must not be in the past."))
        : Result.Ok();

    public Result ValidateChargingStart(DateTimeOffset now)
    {
        if (State != ReservationState.Active)
        {
            return Result.Fail(
                new ChargingStartError(
                    ChargingStartError.ReservationInactiveCode,
                    "Only active reservations can start charging."));
        }

        if (now < StartsAt)
        {
            return Result.Fail(
                new ChargingStartError(
                    ChargingStartError.TooEarlyCode,
                    "Charging cannot start before the booked slot."));
        }

        if (now >= ArrivalDeadline || now >= EndsAt)
        {
            return Result.Fail(
                new ChargingStartError(ChargingStartError.WindowEndedCode, "The arrival deadline has passed."));
        }

        return Result.Ok();
    }

    public Result Cancel(DateTimeOffset now, bool hasUnresolvedStart)
    {
        if (State != ReservationState.Active)
        {
            return Result.Fail(new ReservationCancellationError("Only active reservations can be cancelled."));
        }

        if (hasUnresolvedStart)
        {
            return Result.Fail(new ReservationCancellationError(
                "A reservation with a pending or uncertain start cannot be cancelled."));
        }

        State = ReservationState.Cancelled;
        CancelledAt = now.ToUniversalTime();
        return Result.Ok();
    }

    public Result Consume()
    {
        if (State != ReservationState.Active)
        {
            return Result.Fail(new ChargingStartError(
                ChargingStartError.ReservationInactiveCode, "Only an active reservation can be consumed."));
        }

        State = ReservationState.Consumed;
        return Result.Ok();
    }

    public ReservationExpiryOutcome ExpireAtArrivalDeadline(DateTimeOffset now, bool hasUnresolvedStart)
    {
        if (State != ReservationState.Active)
        {
            return ReservationExpiryOutcome.AlreadyInactive;
        }

        if (now < ArrivalDeadline)
        {
            return ReservationExpiryOutcome.NotDue;
        }

        if (hasUnresolvedStart)
        {
            return ReservationExpiryOutcome.UnresolvedStart;
        }

        State = ReservationState.Expired;
        ExpiredAt = now.ToUniversalTime();
        return ReservationExpiryOutcome.Expired;
    }
}
