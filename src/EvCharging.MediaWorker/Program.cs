using EvCharging.S3ObjectStore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddObjectStore();

using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("MediaWorker")
    .LogInformation("Media/document worker stub; no media or documents are processed.");

await host.RunAsync();
