using EvCharging.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EvCharging.Persistence.Extensions;

public static class ExceptionExtensions
{
    public static bool IsChargingClaimConflict(this DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ChargingSessionConfiguration.ConnectorClaimIndexName
                or ChargingSessionConfiguration.ReservationClaimIndexName
        };

    public static bool IsReservationNotificationConflict(this Exception exception) => exception switch
    {
        PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReservationNotificationConfiguration.ReservationKindIndexName
        } => true,
        DbUpdateException { InnerException: { } cause } => cause.IsReservationNotificationConflict(),
        _ => false
    };

    public static bool IsDriverIdentityConflict(this DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: DriverConfiguration.IdentityIndexName
        };

    public static bool IsTransientDatabaseFailure(this Exception exception) => exception switch
    {
        NpgsqlException postgres => postgres.IsTransient,
        DbUpdateException { InnerException: { } cause } => cause.IsTransientDatabaseFailure(),
        InvalidOperationException { InnerException: { } cause } => cause.IsTransientDatabaseFailure(),
        _ => false
    };

    public static bool IsReservationOverlap(this DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: "ex_reservations_connector_slot"
        };

    public static bool IsDriverReservationOverlap(this DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: "ex_reservations_driver_slot"
        };
}
