using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.ObservationStream;
using EvCharging.ObservationWorker.Projection;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddDocumentStore();
builder.Services.AddOptions<ObservationClockSkewOptions>()
    .BindConfiguration(ObservationClockSkewOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.AddObservationStreamConsumer<ObservationProjector>();

await builder.Build().RunAsync();
