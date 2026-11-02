using System.ComponentModel.DataAnnotations;

namespace EvCharging.AppHost;

/// <summary>
/// The station media limits the AppHost owns and passes to the services that enforce or expose them, which bind
/// the same section and keep no values of their own.
/// </summary>
public sealed class StationMediaOptions
{
    public const string SectionName = "StationMedia";

    [Range(1, int.MaxValue)]
    public int MaximumPhotosPerStation { get; set; }

    [Range(1L, long.MaxValue)]
    public long MaximumPhotoBytes { get; set; }

    [Range(1L, long.MaxValue)]
    public long MaximumPhotoPixels { get; set; }

    [MinLength(1)]
    public string[] PhotoContentTypes { get; set; } = [];

    [Range(1, int.MaxValue)]
    public int MaximumVideosPerStation { get; set; }

    [Range(1L, long.MaxValue)]
    public long MaximumVideoBytes { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan MaximumVideoDuration { get; set; }

    [MinLength(1)]
    public string[] VideoContentTypes { get; set; } = [];

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan UploadUrlLifetime { get; set; }
}
