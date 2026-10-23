using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Reservations;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Charging;

/// <summary>What the session queries need of a session the driver owns.</summary>
internal sealed record OwnedChargingSession(
    Guid PublicId,
    Guid ReservationId,
    SessionLifecycle Lifecycle,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    decimal? FinalEnergyKWh,
    string? IntegrationKey);

internal static class ChargingSessionQueryExtensions
{
    public static async Task<Result<OwnedChargingSession>> LoadOwnedSessionAsync(
        this PlatformDbContext database, Guid sessionId, UserIdentity identity, CancellationToken cancellationToken)
    {
        var session = await database.Sessions
            .AsNoTracking()
            .Where(s => s.PublicId == sessionId)
            .Select(
                s => new
                {
                    s.PublicId,
                    ReservationId = s.Reservation!.PublicId,
                    s.Lifecycle,
                    s.StartedAt,
                    s.CompletedAt,
                    s.FinalEnergyKWh,
                    IsOwnedByCaller = database.Reservations
                        .OwnedBy(identity)
                        .Any(reservation => reservation.Id == s.ReservationId),
                    // The start attempt names the integration before the provider has confirmed the session.
                    IntegrationKey = database.Attempts
                        .Where(attempt => attempt.Operation!.SessionId == s.Id)
                        .OrderBy(attempt => attempt.AuthorizedAt)
                        .Select(attempt => attempt.IntegrationKey)
                        .FirstOrDefault()
                })
            .SingleOrDefaultAsync(cancellationToken);
        return session switch
        {
            null => Result.Fail<OwnedChargingSession>(new NotFoundError<Guid>("charging session", sessionId)),
            { IsOwnedByCaller: false } => Result.Fail<OwnedChargingSession>(new ForbiddenError()),
            _ => Result.Ok(new OwnedChargingSession(
                session.PublicId,
                session.ReservationId,
                session.Lifecycle,
                session.StartedAt,
                session.CompletedAt,
                session.FinalEnergyKWh,
                session.IntegrationKey))
        };
    }
}
