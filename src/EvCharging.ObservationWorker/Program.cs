using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.ObservationWorker;
using EvCharging.ObservationWorker.Consumers;
using EvCharging.ObservationWorker.Projection;
using EvCharging.ObservationWorker.Resilience;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddDocumentStore();
builder.Services.AddOptions<ObservationClockSkewOptions>()
    .BindConfiguration(ObservationClockSkewOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
// Consumer settings (group, offset policy, health check) come from the Aspire:Confluent:Kafka:Consumer section.
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
builder.Services.AddSingleton<ObservationProjector>();
builder.Services.AddHostedService<ObservationStreamConsumer>();

await builder.Build().RunAsync();
