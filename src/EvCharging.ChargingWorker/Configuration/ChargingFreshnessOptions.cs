using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Configuration;

/// <summary>The AppHost-owned freshness setting, bound from the shared <c>Charging</c> section.</summary>
public sealed class ChargingFreshnessOptions
{
    public const string SectionName = "Charging";

    /// <summary>A session whose newest reading is older than this is stale.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan FreshnessLimit { get; set; }
}
