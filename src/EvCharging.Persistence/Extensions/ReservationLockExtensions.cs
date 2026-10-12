using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence.Extensions;

public static class ReservationLockExtensions
{
    public static Task LockDriverReservationsAsync(
        this PlatformDbContext database, Guid driverId, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Reservation processing requires an active transaction.");
        }

        return database.Drivers
            .FromSqlInterpolated($"SELECT * FROM charging.drivers WHERE id = {driverId} FOR UPDATE")
            .SingleAsync(cancellationToken);
    }
}
