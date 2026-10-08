var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("ObservationWorker")
    .LogInformation("Observation worker stub; no observations or session evidence are processed.");

await host.RunAsync();
