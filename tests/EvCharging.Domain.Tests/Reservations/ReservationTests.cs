using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Reservations;

public sealed class ReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

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
    [InlineData(-30)]
    [InlineData(30)]
    public void Book_CurrentOrFutureSlot_CreatesActiveReservation(int startMinutes)
    {
        var result = Reservation.Book(
            Connector, Driver, Now.AddMinutes(startMinutes), Now.AddHours(1), Now.ToOffset(TimeSpan.FromHours(5)));

        Assert.True(result.IsSuccess);
        var reservation = result.Value;
        Assert.Equal(ReservationState.Active, reservation.State);
        Assert.Equal(Now, reservation.CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Book_ElapsedSlot_RejectsReservation(int endMinutes)
    {
        var result = Reservation.Book(Connector, Driver, Now.AddHours(-1), Now.AddMinutes(endMinutes), Now);

        var error = Assert.IsType<BookingValidationError>(Assert.Single(result.Errors));
        Assert.Equal(BookingBoundary.End, error.Boundary);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Book_InvalidSlot_RejectsReservation(int endMinutes)
    {
        var result = Reservation.Book(
            Connector, Driver, Now.AddHours(1), Now.AddHours(1).AddMinutes(endMinutes), Now);

        var error = Assert.IsType<BookingValidationError>(Assert.Single(result.Errors));
        Assert.Equal(BookingBoundary.End, error.Boundary);
    }
}
