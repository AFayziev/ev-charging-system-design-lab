using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence.Extensions;

public static class ReservationExpiryExtensions
{
    /// <summary>Stages eligible expiry transitions; the caller holds the driver lock and owns the save.</summary>
    public static async Task<IReadOnlyDictionary<Guid, ReservationExpiryOutcome>> ExpireReservationsAsync(
        this PlatformDbContext database,
        IReadOnlyCollection<Reservation> reservations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var elapsedIds = reservations
            .Where(reservation => reservation.State == ReservationState.Active && reservation.EndsAt <= now)
            .Select(reservation => reservation.Id)
            .ToArray();
        var unresolvedIds = elapsedIds.Length == 0 ? [] : await database.Sessions
            .Where(session => elapsedIds.Contains(session.ReservationId)
                && session.Lifecycle == SessionLifecycle.Starting)
            .Select(session => session.ReservationId)
            .ToListAsync(cancellationToken);
        return reservations.ToDictionary(
            reservation => reservation.Id,
            reservation => reservation.ExpireAtSlotEnd(now, unresolvedIds.Contains(reservation.Id)));
    }
}
