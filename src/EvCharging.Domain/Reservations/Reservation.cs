namespace EvCharging.Domain.Reservations;

public sealed record Reservation(
    long Id,
    long ConnectorId,
    long DriverId,
    BookingInterval Slot,
    ReservationState State);
