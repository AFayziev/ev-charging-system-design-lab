using EvCharging.MediaWorker.Configuration;
using EvCharging.MediaWorker.Persistence;
using EvCharging.Messaging;
using EvCharging.Persistence.Extensions;
using EvCharging.S3ObjectStore;
using Wolverine.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddObjectStore();
builder.AddPlatformPersistence();
builder.Services.AddApplicationServices();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "media");
        options.UseEntityFrameworkCoreTransactions();
        options.AddStationEventRouting();
        options.AddStationMediaRouting();
    });

using var host = builder.Build();

await host.RunAsync();
