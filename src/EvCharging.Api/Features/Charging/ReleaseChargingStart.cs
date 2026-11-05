using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Charging;

public sealed record ReleaseChargingStartCommand(Guid OperationId)
    : IAuthenticatedRequest<ReleasedChargingStartResponse>;

public sealed record ReleasedChargingStartResponse(Guid Id, DateTimeOffset ReleasedAt);

/// <summary>
/// Releases the connector held by an uncertain start for an operator assigned to its station. The release competes
/// with provider evidence under the driver's reservation lock, so whichever commits first decides.
/// </summary>
public sealed class ReleaseChargingStartCommandHandler(
    PlatformDbContext database,
    IAuthorizationService authorization,
    IDbContextOutbox outbox,
    TimeProvider clock,
    IOptions<ChargingStartReleaseOptions> releaseOptions)
{
    public async Task<Result<ReleasedChargingStartResponse>> HandleAsync(
        ReleaseChargingStartCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var startOperationToRelease = await database.Operations.AsNoTracking()
            .Where(operation => operation.PublicId == command.OperationId
                && operation.Kind == ChargingCommandKind.Start)
            .Select(operation => new StartOperationToRelease(
                operation.Id,
                operation.DriverId,
                database.Connectors
                    .Where(connector => connector.Id == operation.Session!.ConnectorId)
                    .Select(connector => connector.Charger!.StationId)
                    .First()))
            .SingleOrDefaultAsync(cancellationToken);

        if (startOperationToRelease is null)
        {
            return Result.Fail<ReleasedChargingStartResponse>(
                new NotFoundError<Guid>("start operation", command.OperationId));
        }

        var station = await database.Stations.AsNoTracking()
            .SingleAsync(station => station.Id == startOperationToRelease.StationId, cancellationToken);
        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);

        if (!permission.Succeeded)
        {
            return Result.Fail<ReleasedChargingStartResponse>(new ForbiddenError());
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.LockDriverReservationsAsync(startOperationToRelease.DriverId, cancellationToken);

        var operation = await database.Operations
            .Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == startOperationToRelease.OperationId, cancellationToken);
        var lastRequestedAt = await database.Attempts
            .Where(attempt => attempt.OperationId == operation.Id)
            .MaxAsync(attempt => (DateTimeOffset?)attempt.AuthorizedAt, cancellationToken);
        bool hasObservations = await database.SessionEvidence
            .AnyAsync(evidence => evidence.SessionId == operation.SessionId, cancellationToken);

        // A start becomes uncertain only after a request; acceptance stands in if none was recorded.
        var now = clock.GetUtcNow();
        var release = operation.ReleaseStart(
            now, lastRequestedAt ?? operation.AcceptedAt, releaseOptions.Value.ReleaseWindow, hasObservations);

        if (release.IsFailed)
        {
            return Result.Fail<ReleasedChargingStartResponse>(release.Errors);
        }

        // A reservation past its arrival deadline was kept only by this start, its one session, so it expires with
        // the release and its remaining time can be booked at once, instead of at the next expiry scan.
        outbox.Enroll(database);
        var reservation = operation.Session!.Reservation!;
        if (reservation.ExpireAtArrivalDeadline(now, hasUnresolvedStart: false) == ReservationExpiryOutcome.Expired)
        {
            await outbox.PublishAsync(new ReservationExpiredEvent(reservation.Id, reservation.ExpiredAt!.Value));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        return Result.Ok(new ReleasedChargingStartResponse(operation.PublicId, operation.ReleasedAt!.Value));
    }

    private sealed record StartOperationToRelease(Guid OperationId, Guid DriverId, Guid StationId);
}
