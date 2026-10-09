using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Reservations;

public sealed class ReservationPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private const long ConnectorId = 1;
    private const long DriverId = 1;

    [Fact]
    public void Conflicts_AdjacentSlots_NoOverlap()
    {
        Reservation reservation = CreateReservation();
        var adjacent = new BookingInterval(reservation.Slot.End, reservation.Slot.End.AddHours(1));

        Assert.False(ReservationPolicy.Conflicts(reservation, ConnectorId, adjacent));
        Assert.False(adjacent.Overlaps(reservation.Slot));
    }

    [Theory]
    [InlineData(ReservationState.Active, true)]
    [InlineData(ReservationState.Consumed, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Expired, false)]
    public void Conflicts_OverlappingSlot_RespectsAllocation(ReservationState state, bool expected)
    {
        Reservation reservation = CreateReservation() with { State = state };
        var overlap = new BookingInterval(Start.AddMinutes(30), Start.AddHours(2));

        Assert.Equal(expected, ReservationPolicy.Conflicts(reservation, ConnectorId, overlap));
        Assert.False(ReservationPolicy.Conflicts(reservation, ConnectorId + 1, overlap));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3599, true)]
    [InlineData(3600, false)]
    public void CanStart_SlotBoundary_UsesHalfOpenInterval(int seconds, bool expected)
    {
        Reservation reservation = CreateReservation();

        Assert.Equal(expected, ReservationPolicy.CanStart(reservation, DriverId, Start.AddSeconds(seconds), false));
    }

    [Theory]
    [InlineData(ReservationState.Consumed)]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Expired)]
    public void CanStart_ReleasedOrUsedReservation_Denied(ReservationState state)
    {
        Reservation reservation = CreateReservation() with { State = state };

        Assert.False(ReservationPolicy.CanStart(reservation, DriverId, Start, false));
    }

    [Fact]
    public void CanStart_WrongOwnerOrOverrun_Denied()
    {
        Reservation reservation = CreateReservation();

        Assert.False(ReservationPolicy.CanStart(reservation, DriverId + 1, Start, false));
        Assert.False(ReservationPolicy.CanStart(reservation, DriverId, Start, true));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(CommandOutcome.Rejected, true)]
    [InlineData(CommandOutcome.Pending, false)]
    [InlineData(CommandOutcome.Uncertain, false)]
    [InlineData(CommandOutcome.Confirmed, false)]
    public void CanCancel_StartOutcome_ProtectsPendingEffect(CommandOutcome? outcome, bool expected)
    {
        Reservation reservation = CreateReservation();

        Assert.Equal(expected, ReservationPolicy.CanCancel(reservation, DriverId, outcome));
        Assert.False(ReservationPolicy.CanCancel(reservation, DriverId + 1, outcome));
        Assert.False(ReservationPolicy.CanCancel(
            reservation with { State = ReservationState.Consumed }, DriverId, outcome));
    }

    [Fact]
    public void BookingInterval_OffsetEndpoints_PreservesActualSlot()
    {
        var slot = new BookingInterval(Start.ToOffset(TimeSpan.FromHours(5)), Start.AddHours(1));

        Assert.Equal(TimeSpan.Zero, slot.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(1), slot.Duration);
        Assert.True(slot.Contains(Start));
        Assert.Throws<ArgumentException>(() => new BookingInterval(Start, Start));
        Assert.Throws<ArgumentException>(() => new BookingInterval(Start, Start.AddSeconds(-1)));
    }

    private static Reservation CreateReservation() =>
        new(
            1,
            ConnectorId,
            DriverId,
            new BookingInterval(Start, Start.AddHours(1)),
            ReservationState.Active);
}
