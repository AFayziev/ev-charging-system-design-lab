using EvCharging.ChargingWorker.Providers.WattHarbor;

namespace EvCharging.ChargingWorker.Providers;

internal static class ChargingProviderExtensions
{
    public static IHostApplicationBuilder AddChargingProviders(this IHostApplicationBuilder builder)
    {
        var integrations = builder.Configuration.GetSection(ChargingProviderIntegrationOptions.SectionName)
            .GetChildren();
        foreach (var section in integrations)
        {
            string key = section.Key;
            builder.Services.AddOptions<ChargingProviderIntegrationOptions>(key)
                .Bind(section)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            var integration = section.Get<ChargingProviderIntegrationOptions>();
            if (integration?.Client == ChargingProviderClientType.WattHarbor)
            {
                builder.AddWattHarborIntegration(key, section);
            }
        }

        builder.Services.AddTransient<ChargingProviderClientFactory>();
        return builder;
    }
}
