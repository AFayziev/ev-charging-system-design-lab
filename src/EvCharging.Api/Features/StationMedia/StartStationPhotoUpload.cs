using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Features.StationMedia;

public sealed record StartStationPhotoUploadRequest(
    [property: Range(0, int.MaxValue)] int GalleryVersion,
    [property: Required, MaxLength(100)] string ContentType,
    long ByteLength);

public sealed record StartStationPhotoUploadCommand(Guid StationId, StartStationPhotoUploadRequest Request)
    : IAuthenticatedRequest<StationPhotoUploadResponse>;

public sealed record StationPhotoUploadResponse(
    Guid ItemId,
    Guid VersionId,
    Uri UploadUrl,
    DateTimeOffset UploadExpiresAt,
    int GalleryVersion);

public sealed class StartStationPhotoUploadCommandHandler(
    PlatformDbContext database,
    IAuthorizationService authorization,
    ObjectStore objectStore,
    TimeProvider clock,
    IOptionsSnapshot<StationMediaOptions> mediaOptions)
{
    public async Task<Result<StationPhotoUploadResponse>> HandleAsync(
        StartStationPhotoUploadCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .SingleOrDefaultAsync(station => station.PublicId == command.StationId, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationPhotoUploadResponse>(new NotFoundError<Guid>("station", command.StationId));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationPhotoUploadResponse>(new ForbiddenError());
        }

        // Items whose uploads failed keep their positions but no longer count toward the limit.
        var failedState = StationMediaVersionState.Failed;
        var galleryOccupancy = await database.StationMediaItems
            .Where(item => item.StationId == station.Id)
            .GroupBy(item => item.StationId)
            .Select(items => new GalleryOccupancy(
                items.Count(item => item.Versions.Any(version => version.State != failedState)),
                items.Max(item => item.Position) + 1))
            .SingleOrDefaultAsync(cancellationToken);
        var request = command.Request;
        var upload = StationMediaItem.StartPhotoUpload(
            station,
            galleryOccupancy?.Count ?? 0,
            galleryOccupancy?.NextPosition ?? 0,
            request.ContentType,
            request.ByteLength,
            mediaOptions.Value.ToDomain(),
            clock.GetUtcNow());
        if (upload.IsFailed)
        {
            return Result.Fail<StationPhotoUploadResponse>(upload.Errors.Select(error => error switch
            {
                StationMediaUploadValidationError validation => validation.ToValidationError(),
                _ => error
            }));
        }

        var mediaItem = upload.Value;
        database.StationMediaItems.Add(mediaItem);
        // Every gallery change advances the gallery version, so the browser's version rejects stale editors and
        // makes concurrent uploads conflict instead of passing the limit together.
        database.Entry(station).Property(station => station.GalleryVersion).OriginalValue = request.GalleryVersion;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<StationPhotoUploadResponse>(new ConcurrencyConflictError());
        }
        catch (DbUpdateException exception) when (exception.IsStationMediaPositionTaken())
        {
            // A concurrent upload took the position before this one's gallery version was checked.
            return Result.Fail<StationPhotoUploadResponse>(new ConcurrencyConflictError());
        }

        // Signing is local, so it follows the commit; an upload left without a URL expires like an
        // abandoned one. The URL expires at the recorded deadline.
        var mediaItemVersion = mediaItem.Versions.Single();
        var uploadUrl = await objectStore.CreateUploadUrlAsync(
            StationMediaObjectKeys.Upload(station.PublicId, mediaItem.PublicId, mediaItemVersion.PublicId),
            mediaItemVersion.ContentType,
            mediaItemVersion.ByteLength,
            mediaItemVersion.UploadExpiresAt);

        return Result.Ok(new StationPhotoUploadResponse(
            mediaItem.PublicId,
            mediaItemVersion.PublicId,
            uploadUrl.Url,
            uploadUrl.ExpiresAt,
            station.GalleryVersion));
    }

    private sealed record GalleryOccupancy(int Count, int NextPosition);
}

internal static class StartStationPhotoUploadMappings
{
    public static ValidationError ToValidationError(this StationMediaUploadValidationError error) => new(
        error.Field switch
        {
            StationMediaUploadField.ContentType => nameof(StartStationPhotoUploadRequest.ContentType),
            StationMediaUploadField.ByteLength => nameof(StartStationPhotoUploadRequest.ByteLength),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Field, "Unknown upload field.")
        },
        error.Message);
}
