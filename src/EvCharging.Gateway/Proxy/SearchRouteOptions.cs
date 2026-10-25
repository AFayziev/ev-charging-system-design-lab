using System.ComponentModel.DataAnnotations;

namespace EvCharging.Gateway.Proxy;

internal sealed class SearchRouteOptions
{
    public const string SectionName = "Search";

    /// <summary>How long the gateway waits for the search service before it answers with a gateway error.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:00:20")]
    public TimeSpan GatewayTimeout { get; set; }
}
