using EvCharging.Domain.Auditing;
using FluentResults;

namespace EvCharging.Domain.Media;

/// <summary>One upload for a gallery item, from its start through verification of the stored original.</summary>
public sealed class StationMediaVersion : IAuditableEntity
{
    private StationMediaVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid ItemId { get; private set; }
    public uint RowVersion { get; private set; }
    public StationMediaVersionState State { get; private set; }
    public string ContentType { get; private set; } = null!;
    public long ByteLength { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset? UploadedAt { get; private set; }
    public StationMediaItem? Item { get; private set; }

    internal static StationMediaVersion AwaitUpload(
        StationMediaItem item, string contentType, long byteLength, TimeSpan uploadLifetime, DateTimeOffset now) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            Item = item,
            State = StationMediaVersionState.AwaitingUpload,
            ContentType = contentType,
            ByteLength = byteLength,
            CreatedAt = now.ToUniversalTime(),
            UploadExpiresAt = (now + uploadLifetime).ToUniversalTime()
        };

    /// <summary>
    /// Records the stored upload when it matches the size and type the upload started with, and fails the upload
    /// otherwise. A finished version stays as it is, so repeated completion notices have no further effect.
    /// </summary>
    public Result CompleteUpload(long storedLength, string? storedContentType, DateTimeOffset now)
    {
        if (State == StationMediaVersionState.AwaitingUpload)
        {
            if (!MatchesStoredMetadata(storedLength, storedContentType))
            {
                State = StationMediaVersionState.Failed;
            }
            else
            {
                State = StationMediaVersionState.Uploaded;
                UploadedAt = now.ToUniversalTime();
            }
        }

        return State == StationMediaVersionState.Failed
            ? Result.Fail(new StationMediaUploadFailedError())
            : Result.Ok();
    }

    /// <summary>
    /// Records that nothing was stored. The upload can still arrive until it expires, and fails after that.
    /// </summary>
    public Result RecordMissingUpload(DateTimeOffset now)
    {
        if (State == StationMediaVersionState.AwaitingUpload && now < UploadExpiresAt)
        {
            return Result.Fail(new StationMediaUploadMissingError());
        }

        if (State == StationMediaVersionState.AwaitingUpload)
        {
            State = StationMediaVersionState.Failed;
        }

        return State == StationMediaVersionState.Failed
            ? Result.Fail(new StationMediaUploadFailedError())
            : Result.Ok();
    }

    /// <summary>Checks a stored object against the size and type this version started with.</summary>
    public bool MatchesStoredMetadata(long storedLength, string? storedContentType) =>
        storedLength == ByteLength && string.Equals(storedContentType, ContentType, StringComparison.Ordinal);
}
