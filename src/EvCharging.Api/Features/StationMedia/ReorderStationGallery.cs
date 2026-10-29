using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Media;
using EvCharging.Messaging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.StationMedia;

public sealed record ReorderStationGalleryRequest(
    [property: Range(0, int.MaxValue)] int GalleryVersion,
    [property: Required] IReadOnlyList<Guid> ItemIds);

public sealed record ReorderStationGalleryCommand(Guid StationId, ReorderStationGalleryRequest Request)
    : IAuthenticatedRequest<StationGalleryVersionResponse>;

public sealed class ReorderStationGalleryCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization)
{
    public async Task<Result<StationGalleryVersionResponse>> HandleAsync(
        ReorderStationGalleryCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .SingleOrDefaultAsync(station => station.PublicId == command.StationId, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationGalleryVersionResponse>(new NotFoundError<Guid>("station", command.StationId));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationGalleryVersionResponse>(new ForbiddenError());
        }

        var positioned = await database.StationMediaItems
            .Where(item => item.StationId == station.Id && item.Position != null)
            .ToListAsync(cancellationToken);
        var request = command.Request;
        var reorder = StationMediaItem.Reorder(station, positioned, request.ItemIds);
        if (reorder.IsFailed)
        {
            return Result.Fail<StationGalleryVersionResponse>(
                new ValidationError(nameof(ReorderStationGalleryRequest.ItemIds), reorder.Errors[0].Message));
        }

        database.Entry(station).Property(station => station.GalleryVersion).OriginalValue = request.GalleryVersion;
        outbox.Enroll(database);
        await outbox.PublishStationChangedAsync(station.Id);
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<StationGalleryVersionResponse>(new ConcurrencyConflictError());
        }

        return Result.Ok(new StationGalleryVersionResponse(station.GalleryVersion));
    }
}
