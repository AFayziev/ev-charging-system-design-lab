using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.StationMedia;

public sealed record CompleteStationMediaUploadCommand(Guid StationId, Guid ItemId)
    : IAuthenticatedRequest<StationMediaUploadStateResponse>;

public sealed record StationMediaUploadStateResponse(Guid ItemId, StationMediaState State);

public sealed class CompleteStationMediaUploadCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization)
{
    public async Task<Result<StationMediaUploadStateResponse>> HandleAsync(
        CompleteStationMediaUploadCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        // The browser's completion notice is a hint: it queues the photo for processing, and the media worker
        // checks the file itself. Repeated notices queue nothing more.
        var mediaItem = await database.StationMediaItems
            .Include(item => item.Station)
            .SingleOrDefaultAsync(
                item => item.PublicId == command.ItemId && item.Station!.PublicId == command.StationId,
                cancellationToken);
        if (mediaItem is null)
        {
            return Result.Fail<StationMediaUploadStateResponse>(new NotFoundError<Guid>("media item", command.ItemId));
        }

        var permission = await authorization.AuthorizeAsync(
            user, mediaItem.Station!, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationMediaUploadStateResponse>(new ForbiddenError());
        }

        var completion = mediaItem.CompleteUpload();
        if (completion.IsFailed)
        {
            return Result.Fail<StationMediaUploadStateResponse>(completion.Errors);
        }

        if (completion.Value)
        {
            outbox.Enroll(database);
            await outbox.PublishAsync(new ProcessStationMediaMessage(mediaItem.Id));
            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The upload deadline or a removal settled the photo first, so its stored state is the answer.
                await database.Entry(mediaItem).ReloadAsync(cancellationToken);
                if (database.Entry(mediaItem).State == EntityState.Detached)
                {
                    return Result.Fail<StationMediaUploadStateResponse>(
                        new NotFoundError<Guid>("media item", command.ItemId));
                }

                if (mediaItem.State == StationMediaState.Uploading)
                {
                    // Another change, such as a reorder, got in between and nothing was queued: the browser repeats it.
                    return Result.Fail<StationMediaUploadStateResponse>(new ConcurrencyConflictError());
                }
            }
        }

        return mediaItem.State == StationMediaState.Failed
            ? Result.Fail<StationMediaUploadStateResponse>(new StationMediaUploadFailedError())
            : Result.Ok(new StationMediaUploadStateResponse(mediaItem.PublicId, mediaItem.State));
    }
}
