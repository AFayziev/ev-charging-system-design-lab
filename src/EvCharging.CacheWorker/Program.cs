var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("CacheWorker")
    .LogInformation("Cache worker stub; no cached views are updated or warmed.");

await host.RunAsync();
