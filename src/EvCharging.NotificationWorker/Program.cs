using EvCharging.NotificationWorker.Configuration;
using EvCharging.NotificationWorker.Handlers;
using EvCharging.NotificationWorker.Persistence;
using EvCharging.Persistence.Extensions;
using EvCharging.ServiceDefaults.Messaging;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddPlatformPersistence();
builder.Services.AddApplicationServices();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "notification");
        options.UseEntityFrameworkCoreTransactions();
        options.AddReservationEventRouting();
        options.Policies.LogMessageStarting(LogLevel.Information);
        options.PublishMessage<SendNotification>().ToRabbitQueue("notification-send");
    });
builder.Services.AddHttpClient(
    "notification-provider-api",
    client => client.BaseAddress = new("https+http://notification-provider-api/"));

using var host = builder.Build();

await host.RunAsync();
