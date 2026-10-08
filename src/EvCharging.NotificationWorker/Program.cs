var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHttpClient("notification-provider-api", client =>
    client.BaseAddress = new("https+http://notification-provider-api/"));

using var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("NotificationWorker")
    .LogInformation("Notification worker stub; no notifications are delivered.");

await host.RunAsync();
