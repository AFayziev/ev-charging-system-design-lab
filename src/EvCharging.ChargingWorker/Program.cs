using EvCharging.Contracts.Events.Reservations;
using EvCharging.Persistence.Extensions;
using Wolverine.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "charging");
        options.ListenToRabbitQueue(ReservationCreated.Queue);
    });
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new("https+http://gateway/api/"));
builder.Services.AddHttpClient(
    "synchronous-charging-provider-api",
    client => client.BaseAddress = new("https+http://synchronous-charging-provider-api/"));
builder.Services.AddHttpClient(
    "queued-charging-provider-api",
    client => client.BaseAddress = new("https+http://queued-charging-provider-api/"));

using var host = builder.Build();

await host.RunAsync();
