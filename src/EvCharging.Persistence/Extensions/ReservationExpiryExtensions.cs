using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence.Extensions;

public static class ReservationExpiryExtensions
{
    /// <summary>Starts with an authorized provider request whose outcome is not yet settled.</summary>
    public static IQueryable<Operation> UnresolvedAuthorizedStarts(this PlatformDbContext database) =>
        database.Operations.Where(operation => operation.Kind == ChargingCommandKind.Start
            && operation.AuthorizedAttemptCount > 0
            && operation.Session!.Lifecycle == SessionLifecycle.Starting);

    /// <summary>
    /// Stages the expiry transition once the reservation's arrival deadline has passed; the caller holds the driver
    /// lock and owns the save. A start with no authorized provider request is rejected locally. A start with an
    /// authorized request keeps its claim and the reservation, because the deadline does not prove the provider did
    /// nothing.
    /// </summary>
    public static async Task<ReservationExpiryOutcome> ExpireReservationAsync(
        this PlatformDbContext database,
        Reservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool unresolvedStart = false;
        if (reservation.State == ReservationState.Active && reservation.ArrivalDeadline <= now)
        {
            var starts = await database.Operations
                .Include(operation => operation.Session)
                .Where(operation => operation.Kind == ChargingCommandKind.Start
                    && operation.Session!.ReservationId == reservation.Id
                    && operation.Session.Lifecycle == SessionLifecycle.Starting)
                .ToListAsync(cancellationToken);
            foreach (var start in starts)
            {
                if (start.AuthorizedAttemptCount > 0)
                {
                    unresolvedStart = true;
                }
                else if (start.RejectStart() is { IsFailed: true } rejection)
                {
                    throw new InvalidOperationException(
                        $"The unexecuted start {start.PublicId} cannot be rejected: {string.Join(", ", rejection.Errors)}");
                }
            }
        }

        return reservation.ExpireAtArrivalDeadline(now, unresolvedStart);
    }
}
