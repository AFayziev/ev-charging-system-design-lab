using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddProviderPersistence();
using var host = builder.Build();
await host.StartAsync();
try
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ProviderDbContext>().Database.MigrateAsync(timeout.Token);
}
finally
{
    await host.StopAsync();
}
