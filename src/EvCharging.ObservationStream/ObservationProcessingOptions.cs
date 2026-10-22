using System.ComponentModel.DataAnnotations;

namespace EvCharging.ObservationStream;

public sealed class ObservationProcessingOptions
{
    public const string SectionName = "ObservationProcessing";

    /// <summary>The most messages handled with one write.</summary>
    [Range(1, 10000)]
    public int BatchSize { get; init; }

    /// <summary>How long a batch waits for more messages once it holds one.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
    public TimeSpan BatchWait { get; init; }

    /// <summary>The first pause before a failed read or write is tried again; later pauses grow from it.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]
    public TimeSpan RetryDelay { get; init; }

    /// <summary>The longest pause between attempts.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "01:00:00")]
    public TimeSpan MaximumRetryDelay { get; init; }
}
