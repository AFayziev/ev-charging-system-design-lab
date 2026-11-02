using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.StationMedia;

public sealed record StartStationMediaUploadRequest(
    [property: Range(0, int.MaxValue)] int GalleryVersion,
    StationMediaKind Kind,
    [property: Required, MaxLength(100)] string ContentType,
    long ByteLength,
    Guid? ReplacesItemId = null);

public sealed record StartStationMediaUploadCommand(Guid StationId, StartStationMediaUploadRequest Request)
    : IAuthenticatedRequest<StationMediaUploadResponse>;

public sealed record StationMediaUploadResponse(
    Guid ItemId,
    Uri UploadUrl,
    DateTimeOffset UploadExpiresAt,
    int GalleryVersion);

public sealed class StartStationMediaUploadCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization,
    ObjectStore objectStore,
    TimeProvider clock,
    IOptionsSnapshot<StationMediaOptions> mediaOptions)
{
    public async Task<Result<StationMediaUploadResponse>> HandleAsync(
        StartStationMediaUploadCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .SingleOrDefaultAsync(station => station.PublicId == command.StationId, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationMediaUploadResponse>(new NotFoundError<Guid>("station", command.StationId));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationMediaUploadResponse>(new ForbiddenError());
        }

        outbox.Enroll(database);
        var request = command.Request;
        var upload = request.ReplacesItemId is { } replacesItemId
            ? await StartReplacementAsync(station, replacesItemId, request, cancellationToken)
            : await StartItemAsync(station, request, cancellationToken);
        if (upload.IsFailed)
        {
            return Result.Fail<StationMediaUploadResponse>(upload.Errors.Select(error => error switch
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

        // Signing is local and the URL expires at the recorded deadline. It is made before the commit, so a failure
        // to sign leaves no photo behind.
        var uploadUrl = await objectStore.CreateUploadUrlAsync(
            StationMediaObjectKeys.Upload(station.PublicId, mediaItem.PublicId),
            mediaItem.ContentType,
            mediaItem.ByteLength,
            mediaItem.UploadExpiresAt);

        // The completion notice is only a hint, so the upload deadline settles a photo that never reports in.
        await outbox.ScheduleAsync(new CheckStationMediaUploadMessage(mediaItem.Id), mediaItem.UploadExpiresAt);
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<StationMediaUploadResponse>(new ConcurrencyConflictError());
        }

        return Result.Ok(new StationMediaUploadResponse(
            mediaItem.PublicId, uploadUrl.Url, uploadUrl.ExpiresAt, station.GalleryVersion));
    }

    private async Task<Result<StationMediaItem>> StartItemAsync(
        Domain.Equipment.Station station, StartStationMediaUploadRequest request, CancellationToken cancellationToken)
    {
        // Items that failed keep their positions but no longer count toward their limit. Each kind has its own
        // limit; positions are shared by the whole gallery.
        var failedState = StationMediaState.Failed;
        var kind = request.Kind;
        var galleryOccupancy = await database.StationMediaItems
            .Where(item => item.StationId == station.Id && item.Position != null)
            .GroupBy(item => item.StationId)
            .Select(items => new GalleryOccupancy(
                items.Count(item => item.Kind == kind && item.State != failedState),
                items.Max(item => item.Position)!.Value + 1))
            .SingleOrDefaultAsync(cancellationToken);
        var limits = mediaOptions.Value.ToDomain();
        var count = galleryOccupancy?.Count ?? 0;
        var nextPosition = galleryOccupancy?.NextPosition ?? 0;
        return kind == StationMediaKind.Video
            ? StationMediaItem.StartVideoUpload(
                station, count, nextPosition, request.ContentType, request.ByteLength, limits, clock.GetUtcNow())
            : StationMediaItem.StartPhotoUpload(
                station, count, nextPosition, request.ContentType, request.ByteLength, limits, clock.GetUtcNow());
    }

    private async Task<Result<StationMediaItem>> StartReplacementAsync(
        Domain.Equipment.Station station,
        Guid replacesItemId,
        StartStationMediaUploadRequest request,
        CancellationToken cancellationToken)
    {
        var replaced = await database.StationMediaItems.SingleOrDefaultAsync(
            item => item.PublicId == replacesItemId && item.StationId == station.Id, cancellationToken);
        if (replaced is null)
        {
            return Result.Fail<StationMediaItem>(new NotFoundError<Guid>("media item", replacesItemId));
        }

        var replacement = StationMediaItem.StartReplacement(
            station,
            replaced,
            request.ContentType,
            request.ByteLength,
            mediaOptions.Value.ToDomain(),
            clock.GetUtcNow());
        if (replacement.IsFailed)
        {
            return replacement;
        }

        // An item has one replacement at a time; an unfinished earlier one is dropped along with its files.
        var unfinished = await database.StationMediaItems
            .Where(item => item.ReplacesItemId == replaced.Id)
            .ToListAsync(cancellationToken);
        database.StationMediaItems.RemoveRange(unfinished);
        foreach (var item in unfinished)
        {
            await outbox.PublishAsync(new DeleteStationMediaObjectsMessage(station.PublicId, item.PublicId));
        }

        return replacement;
    }

    private sealed record GalleryOccupancy(int Count, int NextPosition);
}

internal static class StartStationMediaUploadMappings
{
    public static ValidationError ToValidationError(this StationMediaUploadValidationError error) => new(
        error.Field switch
        {
            StationMediaUploadField.ContentType => nameof(StartStationMediaUploadRequest.ContentType),
            StationMediaUploadField.ByteLength => nameof(StartStationMediaUploadRequest.ByteLength),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Field, "Unknown upload field.")
        },
        error.Message);
}
