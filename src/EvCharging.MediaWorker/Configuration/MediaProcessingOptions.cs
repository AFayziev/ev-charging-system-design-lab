using System.ComponentModel.DataAnnotations;

namespace EvCharging.MediaWorker.Configuration;

public sealed class MediaProcessingOptions
{
    public const string SectionName = "MediaProcessing";

    /// <summary>The longest edge of the small variant shown in lists, in pixels.</summary>
    [Range(16, 4096)]
    public int ThumbnailEdge { get; set; }

    /// <summary>The longest edge of the variant shown on the station page, in pixels.</summary>
    [Range(16, 4096)]
    public int DisplayEdge { get; set; }

    [Range(1, 100)]
    public int WebpQuality { get; set; }

    /// <summary>How many photos are decoded at once; each decoded photo can take about 100 MB.</summary>
    [Range(1, 16)]
    public int MaximumConcurrentProcessing { get; set; }

    /// <summary>How long to wait before looking again for an upload the browser reported but storage doesn't have yet.</summary>
    public TimeSpan MissingUploadRetryDelay { get; set; }

    [MinLength(1), MaxLength(8)]
    public TimeSpan[] RetryDelays { get; set; } = [];
}
