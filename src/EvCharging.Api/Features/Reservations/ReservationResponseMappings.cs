using EvCharging.Api.Features.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;

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
            reservation.ExpiredAt,
            reservation.ArrivalDeadline);
    }

    public static async Task<IReadOnlyList<ReservationResponse>> ToResponsesAsync(
        this IReadOnlyCollection<Reservation> reservations,
        PlatformDbContext database,
        CancellationToken cancellationToken)
    {
        var ids = reservations.Select(value => value.Id).ToArray();
        var operations = await database.Operations.AsNoTracking().Include(value => value.Session)
            .Where(value => value.Kind == ChargingCommandKind.Start && value.Session != null
                && ids.Contains(value.Session.ReservationId))
            .OrderByDescending(value => value.AcceptedAt)
            .ThenByDescending(value => value.Id)
            .ToListAsync(cancellationToken);
        var latest = operations.GroupBy(value => value.Session!.ReservationId)
            .ToDictionary(group => group.Key, group => group.First().ToResponse());
        return reservations.Select(
            value => value.ToResponse() with { StartOperation = latest.GetValueOrDefault(value.Id) })
            .ToArray();
    }
}
