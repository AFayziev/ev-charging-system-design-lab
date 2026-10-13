using EvCharging.Domain.Reservations;

namespace EvCharging.Api.Features.Reservations;

internal static class ReservationResponseMappings
{
    public static ReservationResponse ToResponse(this Reservation reservation)
    {
        var connector = reservation.Connector ?? throw new InvalidOperationException("Connector must be loaded.");
        var charger = connector.Charger ?? throw new InvalidOperationException("Charger must be loaded.");
        var station = charger.Station ?? throw new InvalidOperationException("Station must be loaded.");
        return new(
            reservation.PublicId,
            station.PublicId,
            station.Name,
            station.Address,
            station.TimeZoneId,
            connector.PublicId,
            charger.Name,
            connector.Number,
            connector.ConnectorType,
            connector.PowerSupply,
            connector.RatedPowerKW,
            reservation.StartsAt,
            reservation.EndsAt,
            reservation.State,
            reservation.CreatedAt,
            reservation.CancelledAt,
            reservation.ExpiredAt);
    }
}
