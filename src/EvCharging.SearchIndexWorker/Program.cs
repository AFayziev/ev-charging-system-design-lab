var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("SearchIndexWorker")
    .LogInformation("Search index worker stub; no indexes are updated or rebuilt.");

await host.RunAsync();
