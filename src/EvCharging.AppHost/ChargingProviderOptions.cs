using System.ComponentModel.DataAnnotations;

namespace EvCharging.AppHost;

public sealed class ChargingProviderOptions
{
    public const string SectionName = "ChargingProviders";

    [Required]
    public string WattHarborIntegrationKey { get; set; } = "";

    [Required]
    public string AmpTrailIntegrationKey { get; set; } = "";
}
