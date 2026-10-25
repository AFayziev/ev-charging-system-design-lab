using System.ComponentModel.DataAnnotations;

namespace EvCharging.SearchApi.Configuration;

internal sealed class SearchApiOptions
{
    public const string SectionName = "Search";

    /// <summary>A slow index answers as unavailable instead of holding the caller.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:05")]
    public TimeSpan RequestTimeout { get; set; }

    [Range(0, 2)]
    public int RetryAttempts { get; set; }

    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:00.500")]
    public TimeSpan RetryDelay { get; set; }
}
