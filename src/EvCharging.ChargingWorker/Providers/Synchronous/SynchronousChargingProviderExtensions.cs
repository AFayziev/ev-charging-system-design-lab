using Duende.AccessTokenManagement;
using Microsoft.Extensions.Options;

namespace EvCharging.ChargingWorker.Providers.Synchronous;

internal static class SynchronousChargingProviderExtensions
{
    internal const string TokenClientName = "synchronous-charging-provider";

    public static IHostApplicationBuilder AddSynchronousChargingProvider(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<SynchronousChargingProviderOptions>()
            .BindConfiguration(SynchronousChargingProviderOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out var endpoint)
                    && endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0
                    && (endpoint.Scheme == "https"
                        || (options.AllowLocalHttp && endpoint.IsLoopback && endpoint.Scheme == "http")),
                "The provider token endpoint requires HTTPS or explicit loopback HTTP.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero && options.RequestTimeout <= TimeSpan.FromMinutes(1),
                "The provider request timeout must be positive and at most one minute.")
            .ValidateOnStart();
        builder.Services.AddClientCredentialsTokenManagement();
        builder.Services.AddOptions<ClientCredentialsClient>(TokenClientName)
            .Configure<IOptions<SynchronousChargingProviderOptions>>((client, settings) =>
            {
                client.TokenEndpoint = new Uri(settings.Value.TokenEndpoint);
                client.ClientId = ClientId.Parse(settings.Value.ClientId);
                client.ClientSecret = ClientSecret.Parse(settings.Value.ClientSecret);
            });
        builder.Services.AddHttpClient(ClientCredentialsTokenManagementDefaults.BackChannelHttpClientName)
            .ConfigureHttpClient((services, client) => client.Timeout = services
                .GetRequiredService<IOptions<SynchronousChargingProviderOptions>>().Value.RequestTimeout);
        builder.Services.AddHttpClient<SynchronousChargingProviderClient>((services, client) =>
        {
            client.BaseAddress = new Uri("https+http://synchronous-charging-provider-api/");
            client.Timeout = services.GetRequiredService<IOptions<SynchronousChargingProviderOptions>>()
                .Value.RequestTimeout;
        })
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse(TokenClientName));
        return builder;
    }
}
