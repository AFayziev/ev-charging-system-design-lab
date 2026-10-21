using System.ComponentModel.DataAnnotations;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

internal sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Spacing of observations for an active session.</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "01:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How often due observations are looked up for delivery.</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "01:00:00")]
    public TimeSpan DeliveryPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Base delay before a failed delivery is retried; it grows with each attempt.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "01:00:00")]
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    [Range(1, 100)]
    public int MaxDeliveryAttempts { get; init; } = 5;

    /// <summary>Observations older than this are no longer delivered, so a late target gets no stale flood.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan DeliveryHorizon { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The most observations one callback carries; the target's batch limit must not be lower.</summary>
    [Range(1, 1000)]
    public int DeliveryBatchSize { get; init; } = 100;
}
