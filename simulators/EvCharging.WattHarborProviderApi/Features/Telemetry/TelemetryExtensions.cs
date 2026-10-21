using Microsoft.Extensions.Http.Resilience;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

internal static class TelemetryExtensions
{
    public static IHostApplicationBuilder AddObservationTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<TelemetryOptions>()
            .BindConfiguration(TelemetryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHttpClient(ObservationDeliverer.HttpClientName);
        // The platform confirms a callback only after its stream append (8 s producer timeout), so the timeouts sit
        // above that. POST retries stay disabled because the deliverer retries with the same event ID.
        builder.Services.Configure<HttpStandardResilienceOptions>(
            $"{ObservationDeliverer.HttpClientName}-standard",
            options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
            });
        builder.Services.AddHostedService<TelemetryEmitter>();
        builder.Services.AddHostedService<ObservationDeliverer>();
        return builder;
    }
}
