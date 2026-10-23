using EvCharging.Contracts.Charging;
using EvCharging.Contracts.Events.Sessions;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed class CompleteChargingSessionHandler
{
    /// <summary>
    /// Completes the session from its recorded final reading. A session that is already finished, or has no final
    /// reading, is left as it is, so a duplicate or replayed job changes nothing.
    /// </summary>
    [Transactional]
    public async Task<SessionCompleted?> HandleAsync(
        CompleteChargingSession message,
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

        return new SessionCompleted(session.Id, session.ReservationId, energyKWh, endedAt);
    }
}
