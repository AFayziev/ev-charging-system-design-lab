using EvCharging.ObjectStorage;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddObjectStorage();

using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("MediaWorker")
    .LogInformation("Media/document worker stub; no media or documents are processed.");

await host.RunAsync();
