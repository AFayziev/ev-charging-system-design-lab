using EvCharging.Domain.Auditing;
using EvCharging.Domain.Equipment;
using FluentResults;

namespace EvCharging.Domain.Media;

/// <summary>
/// A photo in a station's gallery, from the start of its upload to its publication. A replacement is a photo
/// of its own that takes over the place of the photo it replaces when it publishes.
/// </summary>
public sealed class StationMediaItem : IAuditableEntity
{
    private StationMediaItem()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid StationId { get; private set; }
    public StationMediaKind Kind { get; private set; }

    /// <summary>The place in the gallery; a replacement has none until it takes over the place it replaces.</summary>
    public int? Position { get; private set; }

    public Guid? ReplacesItemId { get; private set; }
    public StationMediaState State { get; private set; }
    public string ContentType { get; private set; } = null!;
    public long ByteLength { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public uint RowVersion { get; private set; }
    public Station? Station { get; private set; }

    /// <summary>
    /// Starts a photo upload at the gallery's next position. The gallery count covers photos that have not failed
    /// and are not replacements. The station's gallery version advances with the change.
    /// </summary>
    public static Result<StationMediaItem> StartPhotoUpload(
        Station station,
        int galleryItemCount,
        int nextPosition,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now)
    {
        var validation = ValidatePhoto(contentType, byteLength, limits);
        if (validation.IsFailed)
        {
            return validation.ToResult<StationMediaItem>();
        }

        if (galleryItemCount >= limits.MaximumPhotosPerStation)
        {
            return Result.Fail<StationMediaItem>(new StationMediaLimitError(
                $"A station can have at most {limits.MaximumPhotosPerStation} photos."));
        }

        return Result.Ok(AwaitUpload(station, contentType, byteLength, limits, now, nextPosition, null));
    }

    /// <summary>
    /// Starts the upload of a photo that replaces a published one. It does not count toward the limit, and the
    /// replaced photo stays as it is until the replacement publishes.
    /// </summary>
    public static Result<StationMediaItem> StartPhotoReplacement(
        Station station,
        StationMediaItem replaced,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now)
    {
        if (replaced.State != StationMediaState.Published)
        {
            return Result.Fail<StationMediaItem>(new StationMediaStateError("Only a published photo can be replaced."));
        }

        var validation = ValidatePhoto(contentType, byteLength, limits);
        if (validation.IsFailed)
        {
            return validation.ToResult<StationMediaItem>();
        }

        return Result.Ok(AwaitUpload(station, contentType, byteLength, limits, now, null, replaced.Id));
    }

    /// <summary>
    /// Rewrites the gallery positions to the submitted order, which must list every positioned photo once.
    /// The station's gallery version advances with the change.
    /// </summary>
    public static Result Reorder(
        Station station, IReadOnlyCollection<StationMediaItem> positioned, IReadOnlyList<Guid> orderedPublicIds)
    {
        var byPublicId = positioned.ToDictionary(item => item.PublicId);
        if (orderedPublicIds.Count != byPublicId.Count
            || orderedPublicIds.Distinct().Count() != orderedPublicIds.Count
            || !orderedPublicIds.All(byPublicId.ContainsKey))
        {
            return Result.Fail(new StationMediaOrderError());
        }

        for (int position = 0; position < orderedPublicIds.Count; position++)
        {
            byPublicId[orderedPublicIds[position]].Position = position;
        }

        station.AdvanceGalleryVersion();
        return Result.Ok();
    }

    /// <summary>
    /// Takes this photo and its unfinished replacements out of the gallery, and returns them for the caller to
    /// delete. The station's gallery version advances with the change.
    /// </summary>
    public IReadOnlyList<StationMediaItem> RemoveFromGallery(
        Station station, IReadOnlyCollection<StationMediaItem> unfinishedReplacements)
    {
        station.AdvanceGalleryVersion();
        return [this, .. unfinishedReplacements];
    }

    /// <summary>
    /// Takes the browser's completion notice as a hint to process the upload; the worker verifies the file.
    /// The value says whether this notice moved the photo on to processing: repeated notices change nothing, and
    /// a failed photo reports its failure.
    /// </summary>
    public Result<bool> CompleteUpload()
    {
        if (State == StationMediaState.Failed)
        {
            return Result.Fail<bool>(new StationMediaUploadFailedError());
        }

        bool moved = State == StationMediaState.Uploading;
        if (moved)
        {
            State = StationMediaState.Processing;
        }

        return Result.Ok(moved);
    }

    /// <summary>
    /// Settles an upload that is still unfinished at its deadline: it goes on to processing when the file
    /// arrived, and otherwise fails. Returns whether the photo needs processing; a photo that is no longer
    /// uploading is left as it is.
    /// </summary>
    public bool ResolveExpiredUpload(bool fileExists)
    {
        if (State != StationMediaState.Uploading)
        {
            return false;
        }

        if (fileExists)
        {
            State = StationMediaState.Processing;
            return true;
        }

        State = StationMediaState.Failed;
        FailureCode = StationMediaFailure.UploadExpired;
        return false;
    }

    /// <summary>Records that the photo cannot be published; it no longer counts toward the photo limit.</summary>
    public Result Fail(string code)
    {
        if (State is not (StationMediaState.Uploading or StationMediaState.Processing))
        {
            return Result.Fail(new StationMediaStateError($"A {State} photo cannot fail."));
        }

        State = StationMediaState.Failed;
        FailureCode = code;
        return Result.Ok();
    }

    /// <summary>
    /// Publishes a processed photo. A replacement takes over the position of the photo it replaces, which the
    /// caller then removes.
    /// </summary>
    public Result Publish(StationMediaItem? replaced, DateTimeOffset now)
    {
        if (State != StationMediaState.Processing)
        {
            return Result.Fail(new StationMediaStateError($"A {State} photo cannot be published."));
        }

        if (ReplacesItemId is { } replacedId)
        {
            if (replaced is null || replaced.Id != replacedId)
            {
                return Result.Fail(new StationMediaStateError("The photo this one replaces is required."));
            }

            Position = replaced.Position;
            ReplacesItemId = null;
        }

        State = StationMediaState.Published;
        PublishedAt = now.ToUniversalTime();
        return Result.Ok();
    }

    private static Result ValidatePhoto(
        string contentType, long byteLength, StationMediaLimits limits)
    {
        if (!limits.PhotoContentTypes.Contains(contentType))
        {
            return Result.Fail(new StationMediaUploadValidationError(
                StationMediaUploadField.ContentType,
                $"Photos must be one of: {string.Join(", ", limits.PhotoContentTypes.Order(StringComparer.Ordinal))}."));
        }

        if (byteLength <= 0 || byteLength > limits.MaximumPhotoBytes)
        {
            return Result.Fail(new StationMediaUploadValidationError(
                StationMediaUploadField.ByteLength,
                $"Photos must be between 1 and {limits.MaximumPhotoBytes} bytes."));
        }

        return Result.Ok();
    }

    private static StationMediaItem AwaitUpload(
        Station station,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now,
        int? position,
        Guid? replacesItemId)
    {
        station.AdvanceGalleryVersion();
        return new StationMediaItem
        {
            Position = position,
            ReplacesItemId = replacesItemId,
            PublicId = Guid.NewGuid(),
            Station = station,
            StationId = station.Id,
            Kind = StationMediaKind.Photo,
            State = StationMediaState.Uploading,
            ContentType = contentType,
            ByteLength = byteLength,
            CreatedAt = now.ToUniversalTime(),
            UploadExpiresAt = (now + limits.UploadLifetime).ToUniversalTime()
        };
    }
}
