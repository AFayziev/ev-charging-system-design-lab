using EvCharging.Contracts.Charging;
using EvCharging.Contracts.Events.Sessions;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class CompleteChargingSessionMessageHandler
{
    /// <summary>
    /// Completes the session from its recorded final reading. A session that is already finished, or has no final
    /// reading, is left as it is, so a duplicate or replayed job changes nothing.
    /// </summary>
    [Transactional]
    public async Task<SessionCompletedEvent?> HandleAsync(
        CompleteChargingSessionMessage message,
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        var driverId = await database.Sessions
            .Where(session => session.Id == message.SessionId)
            .Select(session => (Guid?)session.Reservation!.DriverId)
            .SingleOrDefaultAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        await database.LockDriverReservationsAsync(driverId.Value, cancellationToken);
        var session = await database.Sessions.SingleAsync(value => value.Id == message.SessionId, cancellationToken);
        var evidence = await database.SessionEvidence
            .SingleOrDefaultAsync(value => value.SessionId == message.SessionId, cancellationToken);
        if (evidence is not { FinalEnergyKWh: { } energyKWh, FinalReadingAt: { } endedAt }
            || session.Complete(energyKWh, endedAt).IsFailed)
        {
            return null;
        }

        // A stop that had not settled has nothing left to do: the end state it asked for holds.
        var unsettledStops = await database.Operations
            .Where(operation => operation.SessionId == session.Id && operation.Kind == ChargingCommandKind.Stop
                && (operation.Outcome == CommandOutcome.Pending || operation.Outcome == CommandOutcome.Uncertain))
            .ToListAsync(cancellationToken);
        foreach (var stop in unsettledStops)
        {
            stop.ConfirmStop();
        }

        return new SessionCompletedEvent(session.Id, session.ReservationId, energyKWh, endedAt);
    }
}
