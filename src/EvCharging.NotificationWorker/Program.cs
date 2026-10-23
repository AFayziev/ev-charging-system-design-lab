using EvCharging.Messaging;
using EvCharging.NotificationWorker.Configuration;
using EvCharging.NotificationWorker.Delivery;
using EvCharging.NotificationWorker.Handlers;
using EvCharging.NotificationWorker.Persistence;
using EvCharging.Persistence.Extensions;
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
        options.CodeGeneration.AlwaysUseServiceLocationFor<INotificationDelivery>();
        options.AddReservationEventRouting();
        options.Policies.LogMessageStarting(LogLevel.Information);
        options.PublishMessage<SendNotificationMessage>().ToRabbitQueue("notification-send");
    });

using var host = builder.Build();

await host.RunAsync();
