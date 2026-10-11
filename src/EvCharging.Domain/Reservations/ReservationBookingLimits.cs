namespace EvCharging.Domain.Reservations;

public sealed record ReservationBookingLimits(
    int MaximumOutstandingReservationsPerDriver,
    TimeSpan MaximumBookingDuration,
    TimeSpan MaximumAdvanceBookingHorizon,
    TimeSpan ArrivalAllowance);
