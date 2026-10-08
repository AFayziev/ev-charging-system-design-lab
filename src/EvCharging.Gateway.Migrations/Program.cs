using EvCharging.Gateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var connection = builder.Configuration.GetConnectionString("gateway-auth-migration-db");
if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException("The gateway-auth-migration-db connection is required.");
}

builder.Services.AddDbContext<AuthenticationDbContext>(options => options.UseNpgsql(
    connection,
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "gateway_auth").CommandTimeout(30)));

using var host = builder.Build();
await host.StartAsync();

try
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));

    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().Database.MigrateAsync(timeout.Token);
}
finally
{
    await host.StopAsync();
}
