using System.ComponentModel.DataAnnotations;

namespace EvCharging.MediaWorker.Configuration;

/// <summary>The station media limits the worker enforces itself; the AppHost owns the section.</summary>
public sealed class StationMediaLimitsOptions
{
    public const string SectionName = "StationMedia";

    [Range(1L, long.MaxValue)]
    public long MaximumPhotoPixels { get; set; }
}
