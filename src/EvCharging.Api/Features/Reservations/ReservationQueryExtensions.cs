using EvCharging.Api.Authentication;
using EvCharging.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

internal static class ReservationQueryExtensions
{
    public static IQueryable<Reservation> OwnedBy(this IQueryable<Reservation> query, UserIdentity identity) =>
        query.Where(reservation => reservation.Driver != null
            && reservation.Driver.IdentityIssuer == identity.Issuer
            && reservation.Driver.IdentitySubject == identity.Subject);

    public static IQueryable<Reservation> WithEquipment(this IQueryable<Reservation> query) =>
        query
            .Include(reservation => reservation.Connector)
            .ThenInclude(connector => connector!.Charger)
            .ThenInclude(charger => charger!.Station);
}
