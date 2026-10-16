using Duende.AccessTokenManagement;
using Microsoft.Extensions.Options;

namespace EvCharging.ChargingWorker.Providers.WattHarbor;

internal static class WattHarborIntegrationExtensions
{
    public static IHostApplicationBuilder AddWattHarborIntegration(
        this IHostApplicationBuilder builder, string key, IConfigurationSection section)
    {
        builder.Services.AddClientCredentialsTokenManagement();
        builder.Services.AddOptions<WattHarborChargingClientOptions>(key)
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(
                options => Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var address)
                    && address.UserInfo.Length == 0 && address.Query.Length == 0 && address.Fragment.Length == 0
                    && address.Scheme is "http" or "https" or "https+http",
                "The provider base address must be an absolute HTTP(S) or discovery address.")
            .Validate(
                options => Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out var endpoint)
                    && endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0
                    && (endpoint.Scheme == "https"
                        || (options.AllowLocalHttp && endpoint.IsLoopback && endpoint.Scheme == "http")),
                "The provider token endpoint requires HTTPS or explicit loopback HTTP.")
            .ValidateOnStart();

        string clientName = ChargingProviderClientFactory.HttpClientName(key);
        string backchannelName = BackchannelName(key);
        builder.Services.AddOptions<ClientCredentialsClient>(clientName)
            .Configure<IOptionsMonitor<WattHarborChargingClientOptions>>((client, settings) =>
            {
                var integration = settings.Get(key);
                client.TokenEndpoint = new Uri(integration.TokenEndpoint);
                client.ClientId = ClientId.Parse(integration.ClientId);
                client.ClientSecret = ClientSecret.Parse(integration.ClientSecret);
                client.HttpClientName = backchannelName;
            });
        builder.Services.AddHttpClient(backchannelName)
            .ConfigureHttpClient((services, client) => client.Timeout = services
                .GetRequiredService<IOptionsMonitor<WattHarborChargingClientOptions>>().Get(key).RequestTimeout);
        builder.Services.AddHttpClient(
            clientName,
            (services, client) =>
            {
                var integration = services.GetRequiredService<IOptionsMonitor<WattHarborChargingClientOptions>>()
                    .Get(key);
                client.BaseAddress = new Uri(integration.BaseAddress);
                client.Timeout = integration.RequestTimeout;
            })
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse(clientName));

        return builder;
    }

    internal static string BackchannelName(string key) => $"wattharbor-token:{key}";
}
