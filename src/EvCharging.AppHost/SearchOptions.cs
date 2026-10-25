using System.ComponentModel.DataAnnotations;

namespace EvCharging.AppHost;

/// <summary>
/// The search settings the AppHost owns and passes to the search service and the gateway, which bind the same
/// section and keep no values of their own.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>How much longer than the service's own budget the gateway waits before it gives up on the service.</summary>
    private static readonly TimeSpan GatewayMargin = TimeSpan.FromSeconds(1);

    /// <summary>How long the service waits for one request to the search index.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:05")]
    public TimeSpan RequestTimeout { get; set; }

    /// <summary>How many times the service repeats a request to the index that failed transiently.</summary>
    [Range(0, 2)]
    public int RetryAttempts { get; set; }

    /// <summary>The pause before each repeat.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:00.500")]
    public TimeSpan RetryDelay { get; set; }

    /// <summary>
    /// How long the gateway waits for the search service: its worst case of every attempt timing out, so the caller
    /// gets the service's typed error rather than a gateway one.
    /// </summary>
    public TimeSpan GatewayTimeout => ((RequestTimeout + RetryDelay) * RetryAttempts) + RequestTimeout + GatewayMargin;
}
