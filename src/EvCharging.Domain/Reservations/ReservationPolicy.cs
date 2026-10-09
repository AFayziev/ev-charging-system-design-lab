using EvCharging.Domain.Charging;

namespace EvCharging.Domain.Reservations;

public static class ReservationPolicy
{
    public static bool CanStart(
        Reservation reservation,
        long driverId,
        DateTimeOffset now,
        bool connectorClaimed) =>
        reservation.State == ReservationState.Active
        && reservation.DriverId == driverId
        && reservation.Slot.Contains(now)
        && !connectorClaimed;

    public static bool CanCancel(
        Reservation reservation,
        long driverId,
        CommandOutcome? startOutcome) =>
        reservation.State == ReservationState.Active
        && reservation.DriverId == driverId
        && startOutcome is null or CommandOutcome.Rejected;

    public static bool Conflicts(Reservation existing, long connectorId, BookingInterval requested) =>
        existing.ConnectorId == connectorId
        && existing.State is ReservationState.Active or ReservationState.Consumed
        && existing.Slot.Overlaps(requested);
}
