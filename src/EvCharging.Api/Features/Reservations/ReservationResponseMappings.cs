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
            .Where(value => value.Session != null && ids.Contains(value.Session.ReservationId))
            .OrderByDescending(value => value.AcceptedAt)
            .ThenByDescending(value => value.Id)
            .Select(
                value => new
                {
                    Operation = value,
                    ResponseStatusCode = database.Attempts
                        .Where(attempt => attempt.OperationId == value.Id)
                        .OrderByDescending(attempt => attempt.Number)
                        .Select(attempt => attempt.ResponseStatusCode)
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);
        var latest = operations
            .GroupBy(value => (value.Operation.Session!.ReservationId, value.Operation.Kind))
            .ToDictionary(group => group.Key, group => group.First());
        return reservations.Select(
            value => value.ToResponse() with
            {
                StartOperation = latest.GetValueOrDefault((value.Id, ChargingCommandKind.Start)) is { } start
                    ? start.Operation.ToResponse(start.ResponseStatusCode)
                    : null,
                StopOperation = latest.GetValueOrDefault((value.Id, ChargingCommandKind.Stop))?.Operation
                    .ToStopResponse()
            })
            .ToArray();
    }
}
