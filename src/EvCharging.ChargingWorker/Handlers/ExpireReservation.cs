using System.Diagnostics;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace EvCharging.ChargingWorker.Handlers;

public sealed record ExpireReservation(Guid ReservationId);

public sealed partial class ExpireReservationHandler
{
    private Guid? _publicId;
    private ReservationExpiryOutcome? _outcome;

    [Transactional]
    public async Task<ReservationExpired?> HandleAsync(
        ExpireReservation message,
        PlatformDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        _publicId = null;
        _outcome = null;

        var driverId = await database.Reservations
            .Where(reservation => reservation.Id == message.ReservationId)
            .Select(reservation => (Guid?)reservation.DriverId)
            .SingleOrDefaultAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        await database.LockDriverReservationsAsync(driverId.Value, cancellationToken);
        var reservation = await database.Reservations
            .SingleOrDefaultAsync(reservation => reservation.Id == message.ReservationId, cancellationToken);
        if (reservation is not null)
        {
            _publicId = reservation.PublicId;
            Activity.Current?.SetTag("reservation.public_id", reservation.PublicId.ToString());
            _outcome = await database.ExpireReservationAsync(reservation, clock.GetUtcNow(), cancellationToken);
            if (_outcome == ReservationExpiryOutcome.Expired)
            {
                return new ReservationExpired(reservation.Id, reservation.ExpiredAt!.Value);
            }
        }

        return null;
    }

    public void AfterCommit(ExpireReservation message, ILogger logger)
    {
        string outcome = _outcome?.ToString() ?? "Missing";
        Activity.Current?.SetTag("reservation.expiry_outcome", outcome);
        LogExpiryChecked(logger, message.ReservationId, _publicId, outcome);
    }

    [LoggerMessage(LogLevel.Information,
        "Reservation expiry check completed for {ReservationId} ({ReservationPublicId}): {Outcome}.")]
    private static partial void LogExpiryChecked(
        ILogger logger, Guid reservationId, Guid? reservationPublicId, string outcome);
}
