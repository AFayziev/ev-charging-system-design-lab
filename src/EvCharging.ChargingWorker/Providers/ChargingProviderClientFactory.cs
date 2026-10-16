using EvCharging.ChargingWorker.Providers.WattHarbor;
using Microsoft.Extensions.Options;

namespace EvCharging.ChargingWorker.Providers;

public sealed class ChargingProviderClientFactory(
    IHttpClientFactory clients,
    IOptionsMonitor<ChargingProviderIntegrationOptions> settings,
    IConfiguration configuration,
    ILoggerFactory loggers)
{
    public void ValidateIntegration(string? key)
    {
        bool configured = !string.IsNullOrWhiteSpace(key)
            && configuration.GetSection(ChargingProviderIntegrationOptions.SectionName).GetChildren()
                .Any(section => string.Equals(section.Key, key, StringComparison.Ordinal));
        if (!configured)
        {
            throw new InvalidOperationException("The charging provider has no configured integration binding.");
        }

        var integration = settings.Get(key!);
        if (integration.Client != ChargingProviderClientType.WattHarbor)
        {
            throw new NotSupportedException($"The charging provider client '{integration.Client}' is not supported.");
        }
    }

    public IChargingProviderClient Create(string key)
    {
        ValidateIntegration(key);
        return settings.Get(key).Client switch
        {
            ChargingProviderClientType.WattHarbor => new WattHarborChargingClient(
                clients.CreateClient(HttpClientName(key)), loggers.CreateLogger<WattHarborChargingClient>()),
            _ => throw new NotSupportedException("The charging provider client is not supported.")
        };
    }

    internal static string HttpClientName(string key) => $"charging-provider:{key}";
}
