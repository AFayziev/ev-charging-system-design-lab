namespace EvCharging.NotificationWorker.Templates.Reservation;

public sealed record ReservationEmailDetails(
    Guid ReservationPublicId,
    DateTimeOffset OccurredAt,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string StationName,
    string StationAddress,
    string ChargerName,
    int ConnectorNumber);
