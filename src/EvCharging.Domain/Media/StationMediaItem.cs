using EvCharging.Domain.Auditing;
using EvCharging.Domain.Equipment;
using FluentResults;

namespace EvCharging.Domain.Media;

/// <summary>
/// A photo or video in a station's gallery, from the start of its upload to its publication. A replacement is an
/// item of its own that takes over the place of the item it replaces when it publishes.
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
        DateTimeOffset now) =>
        StartUpload(
            station, StationMediaKind.Photo, galleryItemCount, nextPosition, contentType, byteLength, limits, now);

    /// <summary>
    /// Starts a video upload at the gallery's next position. The video count covers videos that have not failed
    /// and are not replacements. The station's gallery version advances with the change.
    /// </summary>
    public static Result<StationMediaItem> StartVideoUpload(
        Station station,
        int videoCount,
        int nextPosition,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now) =>
        StartUpload(station, StationMediaKind.Video, videoCount, nextPosition, contentType, byteLength, limits, now);

    /// <summary>
    /// Starts the upload of an item of the same kind that replaces a published one. It does not count toward the
    /// limit, and the replaced item stays as it is until the replacement publishes.
    /// </summary>
    public static Result<StationMediaItem> StartReplacement(
        Station station,
        StationMediaItem replaced,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now)
    {
        if (replaced.State != StationMediaState.Published)
        {
            return Result.Fail<StationMediaItem>(new StationMediaStateError("Only a published item can be replaced."));
        }

        var validation = Validate(KindRules.For(replaced.Kind, limits), contentType, byteLength);
        if (validation.IsFailed)
        {
            return validation.ToResult<StationMediaItem>();
        }

        return Result.Ok(AwaitUpload(
            station, replaced.Kind, contentType, byteLength, limits, now, null, replaced.Id));
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

    /// <summary>Records that the item cannot be published; it no longer counts toward its kind's limit.</summary>
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

    private static Result<StationMediaItem> StartUpload(
        Station station,
        StationMediaKind kind,
        int kindItemCount,
        int nextPosition,
        string contentType,
        long byteLength,
        StationMediaLimits limits,
        DateTimeOffset now)
    {
        var rules = KindRules.For(kind, limits);
        var validation = Validate(rules, contentType, byteLength);
        if (validation.IsFailed)
        {
            return validation.ToResult<StationMediaItem>();
        }

        if (kindItemCount >= rules.MaximumItems)
        {
            return Result.Fail<StationMediaItem>(new StationMediaLimitError(
                $"A station can have at most {rules.MaximumItems} {rules.Limited(rules.MaximumItems)}."));
        }

        return Result.Ok(AwaitUpload(station, kind, contentType, byteLength, limits, now, nextPosition, null));
    }

    private static Result Validate(KindRules rules, string contentType, long byteLength)
    {
        if (!rules.ContentTypes.Contains(contentType))
        {
            return Result.Fail(new StationMediaUploadValidationError(
                StationMediaUploadField.ContentType,
                $"{rules.Subject} must be one of: {string.Join(", ", rules.ContentTypes.Order(StringComparer.Ordinal))}."));
        }

        if (byteLength <= 0 || byteLength > rules.MaximumBytes)
        {
            return Result.Fail(new StationMediaUploadValidationError(
                StationMediaUploadField.ByteLength,
                $"{rules.Subject} must be between 1 and {rules.MaximumBytes} bytes."));
        }

        return Result.Ok();
    }

    /// <summary>The limits and wording that apply to one kind of gallery item.</summary>
    private sealed record KindRules(
        string Subject,
        string Singular,
        string Plural,
        int MaximumItems,
        long MaximumBytes,
        IReadOnlySet<string> ContentTypes)
    {
        public string Limited(int count) => count == 1 ? Singular : Plural;

        public static KindRules For(StationMediaKind kind, StationMediaLimits limits) =>
            kind == StationMediaKind.Video
                ? new(
                    "Videos",
                    "video",
                    "videos",
                    limits.MaximumVideosPerStation,
                    limits.MaximumVideoBytes,
                    limits.VideoContentTypes)
                : new(
                    "Photos",
                    "photo",
                    "photos",
                    limits.MaximumPhotosPerStation,
                    limits.MaximumPhotoBytes,
                    limits.PhotoContentTypes);
    }

    private static StationMediaItem AwaitUpload(
        Station station,
        StationMediaKind kind,
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
            Kind = kind,
            State = StationMediaState.Uploading,
            ContentType = contentType,
            ByteLength = byteLength,
            CreatedAt = now.ToUniversalTime(),
            UploadExpiresAt = (now + limits.UploadLifetime).ToUniversalTime()
        };
    }
}
