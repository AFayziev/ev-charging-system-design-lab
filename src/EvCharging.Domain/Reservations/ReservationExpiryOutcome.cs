namespace EvCharging.Domain.Reservations;

public enum ReservationExpiryOutcome
{
    Expired,
    AlreadyInactive,
    NotDue,
    UnresolvedStart
}
