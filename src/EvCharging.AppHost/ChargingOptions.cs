using System.ComponentModel.DataAnnotations;

namespace EvCharging.AppHost;

/// <summary>
/// The charging settings the AppHost owns and passes to the services, which bind the same section and keep no
/// values of their own.
/// </summary>
public sealed class ChargingOptions : IValidatableObject
{
    public const string SectionName = "Charging";

    /// <summary>How long the API waits for a provider status check before the driver is told it is unavailable.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan StatusCheckTimeout { get; set; }

    /// <summary>A reading older than this is stale, unless it is the provider's final one.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan FreshnessLimit { get; set; }

    /// <summary>A running session silent for longer than this is no longer polled by the driver's page.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan SilenceLimit { get; set; }

    /// <summary>How far a reading's occurrence may lie ahead of the platform clock and still count as current.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan ClockSkew { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SilenceLimit <= FreshnessLimit)
        {
            yield return new ValidationResult(
                "The silence limit must be longer than the freshness limit.",
                [nameof(SilenceLimit), nameof(FreshnessLimit)]);
        }
    }
}
