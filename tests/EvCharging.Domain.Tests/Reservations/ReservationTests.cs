using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Reservations;

public sealed class ReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReservationBookingLimits Limits = new(
        2, TimeSpan.FromHours(1), TimeSpan.FromHours(12), TimeSpan.FromMinutes(10));

    private static readonly Connector Connector = new()
    {
        Id = Guid.NewGuid(),
        ConnectorType = "Type2",
        PowerSupply = "AC"
    };
    private static readonly Driver Driver = new()
    {
        Id = Guid.NewGuid(),
        IdentityIssuer = "https://test.invalid",
        IdentitySubject = "driver"
    };

    [Theory]
    [InlineData(0)]
    [InlineData(720)]
    public void Book_AdmissionBoundary_AcceptsSlot(int startMinutes)
    {
        var start = Now.AddMinutes(startMinutes);
        var result = Reservation.Book(
            Connector, Driver, start, start.AddHours(1), Now.ToOffset(TimeSpan.FromHours(5)), Limits);

        Assert.True(result.IsSuccess);
        var reservation = result.Value;
        Assert.Equal(ReservationState.Active, reservation.State);
        Assert.Equal(Now, reservation.CreatedAt);
        Assert.Equal(start.AddMinutes(10), reservation.ArrivalDeadline);
    }

    [Theory]
    [InlineData(-1, 60, BookingBoundary.Start)]
    [InlineData(721, 780, BookingBoundary.Start)]
    [InlineData(0, 61, BookingBoundary.End)]
    public void Book_OutsideLimits_RejectsSlot(int startMinutes, int endMinutes, BookingBoundary boundary)
    {
        var result = Reservation.Book(
            Connector, Driver, Now.AddMinutes(startMinutes), Now.AddMinutes(endMinutes), Now, Limits);

        var error = Assert.IsType<BookingValidationError>(Assert.Single(result.Errors));
        Assert.Equal(boundary, error.Boundary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Book_InvalidSlot_RejectsReservation(int endMinutes)
    {
        var result = Reservation.Book(
            Connector, Driver, Now.AddHours(1), Now.AddHours(1).AddMinutes(endMinutes), Now, Limits);

        var error = Assert.IsType<BookingValidationError>(Assert.Single(result.Errors));
        Assert.Equal(BookingBoundary.End, error.Boundary);
    }

    [Fact]
    public void ValidateStartTime_StartPassesBeforeAdmission_RejectsWithoutChangingBooking()
    {
        var start = Now.AddMinutes(1);
        var reservation = Reservation.Book(Connector, Driver, start, start.AddHours(1), Now, Limits).Value;

        Assert.True(reservation.ValidateStartTime(start).IsSuccess);
        var result = reservation.ValidateStartTime(start.AddTicks(1));

        var error = Assert.IsType<BookingValidationError>(Assert.Single(result.Errors));
        Assert.Equal(BookingBoundary.Start, error.Boundary);
        Assert.Equal(ReservationState.Active, reservation.State);
        Assert.Equal(Now, reservation.CreatedAt);
        Assert.Equal(start, reservation.StartsAt);
        Assert.Equal(start.AddHours(1), reservation.EndsAt);
        Assert.Equal(start.AddMinutes(10), reservation.ArrivalDeadline);
    }

    [Fact]
    public void Book_ShortSlot_CapsArrivalDeadline()
    {
        var reservation = Reservation.Book(Connector, Driver, Now, Now.AddMinutes(5), Now, Limits).Value;

        Assert.Equal(reservation.EndsAt, reservation.ArrivalDeadline);
    }

    [Fact]
    public void Book_ChangedLimits_ApplyToNewBookingsAndPreserveAcceptedTerms()
    {
        var start = Now.AddHours(1);
        var accepted = Reservation.Book(Connector, Driver, start, start.AddHours(1), Now, Limits).Value;
        var revised = Limits with
        {
            MaximumBookingDuration = TimeSpan.FromMinutes(30),
            MaximumAdvanceBookingHorizon = TimeSpan.FromHours(2),
            ArrivalAllowance = TimeSpan.FromMinutes(3)
        };

        Assert.True(Reservation.Book(Connector, Driver, start, start.AddHours(1), Now, revised).IsFailed);
        Assert.True(Reservation.Book(Connector, Driver, Now.AddHours(3), Now.AddHours(3.5), Now, revised).IsFailed);
        var newlyAccepted = Reservation.Book(Connector, Driver, start, start.AddMinutes(30), Now, revised).Value;

        Assert.Equal(start.AddMinutes(3), newlyAccepted.ArrivalDeadline);
        Assert.Equal(start, accepted.StartsAt);
        Assert.Equal(start.AddHours(1), accepted.EndsAt);
        Assert.Equal(start.AddMinutes(10), accepted.ArrivalDeadline);
        Assert.Equal(ReservationState.Active, accepted.State);
    }

    [Theory]
    [InlineData(false, ReservationState.Expired)]
    [InlineData(true, ReservationState.Active)]
    public void Expire_UnresolvedStart_RetainsAllocation(bool unresolvedStart, ReservationState expected)
    {
        var reservation = Reservation.Book(Connector, Driver, Now, Now.AddHours(1), Now, Limits).Value;

        Assert.False(reservation.TryExpireAtSlotEnd(Now.AddMinutes(10), unresolvedStart));
        reservation.TryExpireAtSlotEnd(Now.AddHours(1), unresolvedStart);

        Assert.Equal(expected, reservation.State);
        Assert.False(reservation.TryExpireAtSlotEnd(Now.AddHours(2), unresolvedStart));
    }
}
