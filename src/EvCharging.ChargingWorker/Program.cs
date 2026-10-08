var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new("https+http://gateway/api/"));
builder.Services.AddHttpClient("synchronous-charging-provider-api", client =>
    client.BaseAddress = new("https+http://synchronous-charging-provider-api/"));
builder.Services.AddHttpClient("queued-charging-provider-api", client =>
    client.BaseAddress = new("https+http://queued-charging-provider-api/"));

using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("ChargingWorker")
    .LogInformation("Charging worker stub; no charging work is executed.");

await host.RunAsync();
