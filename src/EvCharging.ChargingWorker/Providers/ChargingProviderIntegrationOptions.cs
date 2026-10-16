using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Providers;

public sealed class ChargingProviderIntegrationOptions
{
    public const string SectionName = "ChargingProviders:Integrations";

    [Required, EnumDataType(typeof(ChargingProviderClientType))]
    public ChargingProviderClientType? Client { get; set; }
}
