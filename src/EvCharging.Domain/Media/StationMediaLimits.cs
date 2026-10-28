namespace EvCharging.Domain.Media;

public sealed record StationMediaLimits(
    int MaximumPhotosPerStation,
    long MaximumPhotoBytes,
    IReadOnlySet<string> PhotoContentTypes,
    TimeSpan UploadLifetime);
