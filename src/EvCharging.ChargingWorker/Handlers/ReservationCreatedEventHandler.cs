using System.Diagnostics;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public static class ReservationCreatedEventHandler
{
    [Transactional]
    public static async Task<OutgoingMessages> HandleAsync(
        ReservationCreatedEvent message, PlatformDbContext database, CancellationToken cancellationToken)
    {
        var reservation = await database.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(reservation => reservation.Id == message.ReservationId, cancellationToken);
        var outgoing = new OutgoingMessages();
        if (reservation?.State == ReservationState.Active)
        {
            Activity.Current?.SetTag("reservation.public_id", reservation.PublicId.ToString());
            outgoing.Schedule(new ExpireReservationMessage(reservation.Id), reservation.ArrivalDeadline);
        }

        return outgoing;
    }
}
