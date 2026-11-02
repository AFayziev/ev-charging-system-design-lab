namespace EvCharging.Domain.Media;

public sealed record StationMediaLimits(
    int MaximumPhotosPerStation,
    long MaximumPhotoBytes,
    IReadOnlySet<string> PhotoContentTypes,
    int MaximumVideosPerStation,
    long MaximumVideoBytes,
    IReadOnlySet<string> VideoContentTypes,
    TimeSpan UploadLifetime);
