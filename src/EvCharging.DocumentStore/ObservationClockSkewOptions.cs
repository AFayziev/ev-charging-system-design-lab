using System.ComponentModel.DataAnnotations;

namespace EvCharging.DocumentStore;

/// <summary>The clock skew between a provider and the platform that readings are still trusted within.</summary>
public sealed class ObservationClockSkewOptions
{
    public const string SectionName = "Charging";

    /// <summary>How far a reading's occurrence may lie ahead of the platform clock and still count as current.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    public TimeSpan ClockSkew { get; init; }
}
