using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Configuration;

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

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan UploadUrlLifetime { get; set; }
}
