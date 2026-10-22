using EvCharging.Contracts.Observations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EvCharging.ObservationStream;

public static class ObservationStreamExtensions
{
    /// <summary>
    /// Runs one consumer group over the observation stream, handing its batches to <typeparamref name="THandler"/>.
    /// Group, offset policy and health check come from the Aspire:Confluent:Kafka:Consumer section.
    /// </summary>
    public static IHostApplicationBuilder AddObservationStreamConsumer<THandler>(this IHostApplicationBuilder builder)
        where THandler : class, IObservationBatchHandler
    {
        builder.AddKafkaConsumer<string, string>("kafka");
        builder.Services.AddOptions<ObservationStreamOptions>()
            .BindConfiguration(ObservationStreamOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddOptions<ObservationProcessingOptions>()
            .BindConfiguration(ObservationProcessingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddObservationResilience();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IObservationBatchHandler, THandler>();
        builder.Services.AddHostedService<ObservationStreamConsumer>();
        return builder;
    }
}
