using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EvCharging.Persistence.Extensions;

public static class ExceptionExtensions
{
    public static bool IsReservationOverlap(this DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: "ex_reservations_connector_slot"
        };
}
