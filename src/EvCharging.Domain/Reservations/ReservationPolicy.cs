using EvCharging.Domain.Charging;

namespace EvCharging.Domain.Reservations;

public static class ReservationPolicy
{
    public static bool CanStart(
        ReservationState state,
        Guid ownerDriverId,
        BookingInterval slot,
        Guid driverId,
        DateTimeOffset now,
        bool connectorClaimed) =>
        state == ReservationState.Active
        && ownerDriverId == driverId
        && slot.Contains(now)
        && !connectorClaimed;

    public static bool CanCancel(
        ReservationState state,
        Guid ownerDriverId,
        Guid driverId,
        CommandOutcome? startOutcome) =>
        state == ReservationState.Active
        && ownerDriverId == driverId
        && startOutcome is null or CommandOutcome.Rejected;

    public static bool Conflicts(
        ReservationState state,
        Guid existingConnectorId,
        BookingInterval existingSlot,
        Guid connectorId,
        BookingInterval requested) =>
        existingConnectorId == connectorId
        && state is ReservationState.Active or ReservationState.Consumed
        && existingSlot.Overlaps(requested);
}
