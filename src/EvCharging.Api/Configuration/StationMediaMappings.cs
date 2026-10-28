using EvCharging.Domain.Media;

namespace EvCharging.Api.Configuration;

internal static class StationMediaMappings
{
    public static StationMediaLimits ToDomain(this StationMediaOptions options) => new(
        options.MaximumPhotosPerStation,
        options.MaximumPhotoBytes,
        options.PhotoContentTypes.ToHashSet(StringComparer.Ordinal),
        options.UploadUrlLifetime);
}
