using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Providers.WattHarbor;

public sealed class WattHarborChargingClientOptions
{
    [Required]
    public string BaseAddress { get; set; } = "";

    [Required, Url]
    public string TokenEndpoint { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";

    public bool AllowLocalHttp { get; set; }

    [Range(typeof(TimeSpan), "00:00:00", "00:01:00", MinimumIsExclusive = true)]
    public TimeSpan RequestTimeout { get; set; }
}
