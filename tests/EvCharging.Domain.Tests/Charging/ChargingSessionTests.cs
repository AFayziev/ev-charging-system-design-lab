using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Charging;

public sealed class ChargingSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, ChargingStartError.TooEarlyCode)]
    [InlineData(0, null)]
    [InlineData(599, null)]
    [InlineData(600, ChargingStartError.WindowEndedCode)]
    public void AcceptStart_TimeBoundary_PreservesReservationAndRequiresWindow(int seconds, string? errorCode)
    {
        var reservation = Booking();
        var result = ChargingSession.AcceptStart(reservation, Start.AddSeconds(seconds));
        Assert.Equal(ReservationState.Active, reservation.State);
        Assert.Null(reservation.CancelledAt);
        if (errorCode is not null)
        {
            Assert.Equal(errorCode, Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
            return;
        }

        Assert.True(result.IsSuccess);
        Assert.Equal(SessionLifecycle.Starting, result.Value.Lifecycle);
        Assert.Equal(reservation.ConnectorId, result.Value.ConnectorId);
        Assert.Equal(Start.AddSeconds(seconds), result.Value.CreatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptStart_InactiveReservation_RejectsWithoutChangingState(bool cancelled)
    {
        var reservation = Booking();
        if (cancelled)
        {
            reservation.Cancel(Start, false);
        }
        else
        {
            reservation.ExpireAtSlotEnd(Start.AddHours(1), false);
        }

        var state = reservation.State;

        var result = ChargingSession.AcceptStart(reservation, Start.AddMinutes(1));

        Assert.Equal(
            ChargingStartError.ReservationInactiveCode,
            Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(state, reservation.State);
    }

    private static Reservation Booking() => Reservation.Book(
        new Connector { Id = Guid.NewGuid(), ConnectorType = "Type2", PowerSupply = "AC" },
        Driver.Register("https://test.invalid", "driver", EmailAddress.Create("driver@example.test").Value),
        Start,
        Start.AddHours(1),
        Start,
        new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
}
