using EvCharging.Domain.Auditing;
using EvCharging.Domain.Equipment;
using FluentResults;

namespace EvCharging.Domain.Media;

/// <summary>An item in a station's gallery. Its versions are the uploads that fill or replace it.</summary>
public sealed class StationMediaItem : IAuditableEntity
{
    private readonly List<StationMediaVersion> _versions = [];

    private StationMediaItem()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid StationId { get; private set; }
    public StationMediaKind Kind { get; private set; }
    public int Position { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Station? Station { get; private set; }
    public IReadOnlyCollection<StationMediaVersion> Versions => _versions;

    /// <summary>
    /// Starts a photo upload at the gallery's next position. The gallery item count covers items still awaiting
    /// their upload but not those whose uploads failed. The station's gallery version advances with the change.
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
        if (!limits.PhotoContentTypes.Contains(contentType))
        {
            return Result.Fail<StationMediaItem>(new StationMediaUploadValidationError(
                StationMediaUploadField.ContentType,
                $"Photos must be one of: {string.Join(", ", limits.PhotoContentTypes.Order(StringComparer.Ordinal))}."));
        }

        if (byteLength <= 0 || byteLength > limits.MaximumPhotoBytes)
        {
            return Result.Fail<StationMediaItem>(new StationMediaUploadValidationError(
                StationMediaUploadField.ByteLength,
                $"Photos must be between 1 and {limits.MaximumPhotoBytes} bytes."));
        }

        if (galleryItemCount >= limits.MaximumPhotosPerStation)
        {
            return Result.Fail<StationMediaItem>(new StationMediaLimitError(
                $"A station can have at most {limits.MaximumPhotosPerStation} photos."));
        }

        var item = new StationMediaItem
        {
            PublicId = Guid.NewGuid(),
            Station = station,
            StationId = station.Id,
            Kind = StationMediaKind.Photo,
            Position = nextPosition,
            CreatedAt = now.ToUniversalTime()
        };
        item._versions.Add(StationMediaVersion.AwaitUpload(item, contentType, byteLength, limits.UploadLifetime, now));
        station.GalleryVersion++;
        return Result.Ok(item);
    }
}
