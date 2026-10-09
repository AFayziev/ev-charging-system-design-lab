using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var connection = builder.Configuration.GetConnectionString("platform-migration");
if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException("The platform-migration connection is required.");
}

builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connection));

using var host = builder.Build();
await host.StartAsync();

try
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));

    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(timeout.Token);
}
finally
{
    await host.StopAsync();
}
