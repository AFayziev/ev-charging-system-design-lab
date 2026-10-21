using EvCharging.DocumentStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddDocumentStore();

using var host = builder.Build();
await host.StartAsync();

try
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));

    // A rejected index definition faults this one-shot, which holds back the resources that wait for it.
    await host.Services.GetRequiredService<ObservationStore>().EnsureIndexesAsync(timeout.Token);
}
finally
{
    await host.StopAsync();
}
