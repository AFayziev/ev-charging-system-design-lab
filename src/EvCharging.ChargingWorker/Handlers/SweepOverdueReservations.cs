using EvCharging.ChargingWorker.Configuration;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed record SweepOverdueReservations;

public static class SweepOverdueReservationsHandler
{
    [Transactional]
    public static async Task<OutgoingMessages> HandleAsync(
        SweepOverdueReservations message,
        PlatformDbContext database,
        TimeProvider clock,
        IOptions<ReservationExpiryOptions> options,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var overdueIds = await database.Reservations
            .Where(reservation => reservation.State == ReservationState.Active && reservation.ArrivalDeadline <= now
                && !database.UnresolvedAuthorizedStarts().Any(start => start.Session!.ReservationId == reservation.Id))
            .OrderBy(reservation => reservation.ArrivalDeadline)
            .ThenBy(reservation => reservation.Id)
            .Select(reservation => reservation.Id)
            .Take(options.Value.SweepBatchSize)
            .ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        foreach (var reservationId in overdueIds)
        {
            outgoing.Add(new ExpireReservation(reservationId));
        }

        return outgoing;
    }
}
