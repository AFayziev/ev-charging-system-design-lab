using EvCharging.Contracts.Events.Reservations;

namespace EvCharging.ChargingWorker.Handlers;

public static partial class ReservationCreatedHandler
{
    public static void Handle(ReservationCreated message, ILogger logger) =>
        LogReservationReceived(logger, message.ReservationId, message.EndsAt);

    [LoggerMessage(LogLevel.Information, "Received reservation {ReservationId}, ending at {EndsAt}.")]
    private static partial void LogReservationReceived(ILogger logger, Guid reservationId, DateTimeOffset endsAt);
}
