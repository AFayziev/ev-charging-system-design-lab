using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Messaging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.StationMedia;

public sealed record RemoveStationMediaCommand(Guid StationId, Guid ItemId)
    : IAuthenticatedRequest<StationGalleryVersionResponse>;

public sealed class RemoveStationMediaCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization)
{
    public async Task<Result<StationGalleryVersionResponse>> HandleAsync(
        RemoveStationMediaCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
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

        // Removing a photo that is already gone changes nothing, so a repeated request is not an error.
        var mediaItem = await database.StationMediaItems.SingleOrDefaultAsync(
            item => item.PublicId == command.ItemId && item.StationId == station.Id, cancellationToken);
        if (mediaItem is null)
        {
            return Result.Ok(new StationGalleryVersionResponse(station.GalleryVersion));
        }

        // The photo's unfinished replacement goes with it, and both lose their stored files.
        var replacements = await database.StationMediaItems
            .Where(item => item.ReplacesItemId == mediaItem.Id)
            .ToListAsync(cancellationToken);
        var removed = mediaItem.RemoveFromGallery(station, replacements);
        database.StationMediaItems.RemoveRange(removed);

        outbox.Enroll(database);
        foreach (var item in removed)
        {
            await outbox.PublishAsync(new DeleteStationMediaObjectsMessage(station.PublicId, item.PublicId));
        }

        if (mediaItem.State == StationMediaState.Published)
        {
            await outbox.PublishStationChangedAsync(station.Id);
        }

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
