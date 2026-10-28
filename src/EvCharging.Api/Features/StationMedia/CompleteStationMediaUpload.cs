using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.StationMedia;

public sealed record CompleteStationMediaUploadCommand(Guid StationId, Guid ItemId, Guid VersionId)
    : IAuthenticatedRequest<StationMediaVersionResponse>;

public sealed record StationMediaVersionResponse(Guid ItemId, Guid VersionId, StationMediaVersionState State);

internal sealed class StationMediaUploadChangedError()
    : Error("The upload was replaced while it was being checked. Send the completion again.")
{
    public const string Code = "upload_changed";
}

public sealed class CompleteStationMediaUploadCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization,
    ObjectStore objectStore,
    TimeProvider clock)
{
    public async Task<Result<StationMediaVersionResponse>> HandleAsync(
        CompleteStationMediaUploadCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        // Treats the browser's completion notice as a hint: the upload is checked against how it started, the verified
        // bytes move out of the expiring upload area, and the version records the kept original with its processing request.
        // An upload that expired without arriving, or arrived different, fails so that it no longer holds a gallery place.

        var mediaItemVersion = await database.StationMediaVersions
            .Include(version => version.Item)
            .ThenInclude(item => item!.Station)
            .SingleOrDefaultAsync(
                version => version.PublicId == command.VersionId
                    && version.Item!.PublicId == command.ItemId
                    && version.Item.Station!.PublicId == command.StationId,
                cancellationToken);
        if (mediaItemVersion is null)
        {
            return Result.Fail<StationMediaVersionResponse>(
                new NotFoundError<Guid>("media item version", command.VersionId));
        }

        var mediaItem = mediaItemVersion.Item!;
        var station = mediaItem.Station!;
        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationMediaVersionResponse>(new ForbiddenError());
        }

        if (mediaItemVersion.State == StationMediaVersionState.AwaitingUpload)
        {
            var storedObjectResult = await KeepOriginalAsync(
                mediaItemVersion, station.PublicId, mediaItem.PublicId, cancellationToken);
            if (storedObjectResult.IsFailed)
            {
                return Result.Fail<StationMediaVersionResponse>(storedObjectResult.Errors);
            }

            var now = clock.GetUtcNow();
            var changeResult = storedObjectResult.Value is { } file
                ? mediaItemVersion.CompleteUpload(file.Length, file.ContentType, now)
                : mediaItemVersion.RecordMissingUpload(now);
            if (mediaItemVersion.State == StationMediaVersionState.AwaitingUpload)
            {
                return Result.Fail<StationMediaVersionResponse>(changeResult.Errors);
            }

            outbox.Enroll(database);
            if (mediaItemVersion.State == StationMediaVersionState.Uploaded)
            {
                await outbox.PublishAsync(new ProcessStationMediaMessage(mediaItemVersion.Id));
            }

            try
            {
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another writer changed the version first, so its stored state is the answer.
                await database.Entry(mediaItemVersion).ReloadAsync(cancellationToken);
            }
        }

        return mediaItemVersion.State == StationMediaVersionState.Failed
            ? Result.Fail<StationMediaVersionResponse>(new StationMediaUploadFailedError())
            : Result.Ok(mediaItemVersion.ToResponse());
    }


    private async Task<Result<StoredObject?>> KeepOriginalAsync(
        StationMediaVersion mediaItemVersion, Guid stationId, Guid itemId, CancellationToken cancellationToken)
    {
        // Returns the stored file the version records, or null when nothing has been stored yet:
        // - A matching upload is copied to the original, only if it hasn't changed since it was checked, because the
        //   upload URL accepts new files until it expires.
        // - An upload that doesn't match is returned without copying, so the version fails on it.
        // - Otherwise an original kept by an earlier attempt is returned, even if the upload has expired since.
        // - An upload that couldn't be copied and left no original was replaced during the check.

        var uploadKey = StationMediaObjectKeys.Upload(stationId, itemId, mediaItemVersion.PublicId);
        var originalKey = StationMediaObjectKeys.Original(stationId, itemId, mediaItemVersion.PublicId);
        var uploadedObject = await objectStore.GetPrivateObjectAsync(uploadKey, cancellationToken);
        if (uploadedObject is not null
            && (!mediaItemVersion.MatchesStoredMetadata(uploadedObject.Length, uploadedObject.ContentType)
                || await objectStore.CopyPrivateAsync(uploadKey, originalKey, uploadedObject.ETag, cancellationToken)))
        {
            return Result.Ok<StoredObject?>(uploadedObject);
        }

        var originalObject = await objectStore.GetPrivateObjectAsync(originalKey, cancellationToken);
        if (originalObject is not null || uploadedObject is null)
        {
            return Result.Ok(originalObject);
        }

        return Result.Fail<StoredObject?>(new StationMediaUploadChangedError());
    }
}

internal static class StationMediaVersionMappings
{
    public static StationMediaVersionResponse ToResponse(this StationMediaVersion version) =>
        new(version.Item!.PublicId, version.PublicId, version.State);
}
