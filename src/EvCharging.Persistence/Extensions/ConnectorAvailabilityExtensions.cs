using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;

namespace EvCharging.Persistence.Extensions;

public static class ConnectorAvailabilityExtensions
{
    /// <summary>Sessions that still hold their connector: starting, active, or stopping.</summary>
    public static IQueryable<ChargingSession> Nonterminal(this IQueryable<ChargingSession> sessions) =>
        sessions.Where(session => ChargingSession.ConnectorHoldingLifecycles.Contains(session.Lifecycle));

    /// <summary>Reservations that apply to their connector and whose slot contains the instant.</summary>
    public static IQueryable<Reservation> ApplicableAt(this IQueryable<Reservation> reservations, DateTimeOffset now) =>
        reservations.Where(reservation =>
            (reservation.State == ReservationState.Active || reservation.State == ReservationState.Consumed)
            && reservation.EndsAt > now
            && reservation.StartsAt <= now);
}
