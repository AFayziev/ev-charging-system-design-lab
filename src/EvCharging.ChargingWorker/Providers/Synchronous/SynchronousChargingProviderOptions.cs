using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Providers.Synchronous;

internal sealed class SynchronousChargingProviderOptions
{
    public const string SectionName = "SynchronousChargingProvider";

    [Required, Url]
    public string TokenEndpoint { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";

    public bool AllowLocalHttp { get; set; }
    public TimeSpan RequestTimeout { get; set; }
}
