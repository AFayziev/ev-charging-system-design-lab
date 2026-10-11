using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Reservations;

public sealed class ReservationPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ConnectorId = Guid.NewGuid();
    private static readonly Guid DriverId = Guid.NewGuid();
    private static readonly BookingInterval Slot = BookingInterval.Create(Start, Start.AddHours(1)).Value;

    [Fact]
    public void Conflicts_AdjacentSlots_NoOverlap()
    {
        var adjacent = BookingInterval.Create(Slot.End, Slot.End.AddHours(1)).Value;

        Assert.False(ReservationPolicy.Conflicts(ReservationState.Active, ConnectorId, Slot, ConnectorId, adjacent));
    }

    [Theory]
    [InlineData(ReservationState.Active, true)]
    [InlineData(ReservationState.Consumed, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Expired, false)]
    public void Conflicts_OverlappingSlot_RespectsAllocation(ReservationState state, bool expected)
    {
        var overlap = BookingInterval.Create(Start.AddMinutes(30), Start.AddHours(2)).Value;

        Assert.Equal(expected, ReservationPolicy.Conflicts(state, ConnectorId, Slot, ConnectorId, overlap));
        Assert.False(ReservationPolicy.Conflicts(state, ConnectorId, Slot, Guid.NewGuid(), overlap));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3599, true)]
    [InlineData(3600, false)]
    public void CanStart_SlotBoundary_UsesHalfOpenInterval(int seconds, bool expected)
    {
        Assert.Equal(
            expected,
            ReservationPolicy.CanStart(
                ReservationState.Active, DriverId, Slot, DriverId, Start.AddSeconds(seconds), false));
    }

    [Theory]
    [InlineData(ReservationState.Consumed)]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Expired)]
    public void CanStart_ReleasedOrUsedReservation_Denied(ReservationState state)
    {
        Assert.False(ReservationPolicy.CanStart(state, DriverId, Slot, DriverId, Start, false));
    }

    [Fact]
    public void CanStart_WrongOwnerOrOverrun_Denied()
    {
        Assert.False(ReservationPolicy.CanStart(ReservationState.Active, DriverId, Slot, Guid.NewGuid(), Start, false));
        Assert.False(ReservationPolicy.CanStart(ReservationState.Active, DriverId, Slot, DriverId, Start, true));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(CommandOutcome.Rejected, true)]
    [InlineData(CommandOutcome.Pending, false)]
    [InlineData(CommandOutcome.Uncertain, false)]
    [InlineData(CommandOutcome.Confirmed, false)]
    public void CanCancel_StartOutcome_ProtectsPendingEffect(CommandOutcome? outcome, bool expected)
    {
        Assert.Equal(expected, ReservationPolicy.CanCancel(ReservationState.Active, DriverId, DriverId, outcome));
        Assert.False(ReservationPolicy.CanCancel(ReservationState.Active, DriverId, Guid.NewGuid(), outcome));
        Assert.False(ReservationPolicy.CanCancel(
            ReservationState.Consumed, DriverId, DriverId, outcome));
    }

}
