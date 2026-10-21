namespace EvCharging.Api.Streaming;

internal static class ObservationStreamExtensions
{
    public static IHostApplicationBuilder AddObservationStream(this IHostApplicationBuilder builder)
    {
        // Producer settings (acks, idempotence, delivery timeout, health check) come from the
        // Aspire:Confluent:Kafka:Producer configuration section.
        builder.AddKafkaProducer<string, string>("kafka");
        builder.Services.AddOptions<ObservationStreamOptions>()
            .BindConfiguration(ObservationStreamOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IObservationStream, KafkaObservationStream>();
        return builder;
    }
}
