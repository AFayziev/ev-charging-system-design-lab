using EvCharging.Messaging;
using EvCharging.Persistence.Extensions;
using EvCharging.SearchIndexWorker.Configuration;
using Wolverine.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddPlatformPersistence();
builder.AddElasticsearchClient("elasticsearch");
builder.Services.AddApplicationServices();
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "search");
        options.UseEntityFrameworkCoreTransactions();
        options.AddStationEventRouting();
    });

builder.Services.AddHostedService<StationSearchIndexStartup>();

using var host = builder.Build();

await host.RunAsync();
