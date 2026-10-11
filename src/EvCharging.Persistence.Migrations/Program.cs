using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using EvCharging.Persistence.Migrations.SeedData;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

if (builder.Configuration["Messaging:ServiceName"] is { } serviceName)
{
    builder.UseWolverine(options =>
    {
        options.ApplicationAssembly = typeof(Program).Assembly;
        options.AddMessageStorage(builder.Configuration, serviceName);
    });
    using var messagingHost = builder.Build();
    return await messagingHost.RunJasperFxCommands(args);
}

var connection = builder.Configuration.GetConnectionString("platform-migration");
if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException("The platform-migration connection is required.");
}

builder.Services.AddOptions<LabDataOptions>()
    .BindConfiguration(LabDataOptions.SectionName)
    .Validate(options => !options.Reset || options.Enabled, "Lab data reset requires seeding to be enabled.")
    .Validate(
        options => !options.Enabled || Uri.TryCreate(options.IdentityIssuer, UriKind.Absolute, out var issuer)
            && issuer.Scheme is "http" or "https",
        "An absolute HTTP(S) LabData:IdentityIssuer is required when seeding is enabled.")
    .ValidateOnStart();

builder.Services.AddDbContext<PlatformDbContext>(
    (services, options) =>
    {
        options.UseNpgsql(connection);

        var labData = services.GetRequiredService<IOptions<LabDataOptions>>().Value;
        if (labData.Enabled)
        {
            options.UseAsyncSeeding((context, _, cancellationToken) =>
                LabDataSeeder.SeedAsync((PlatformDbContext)context, labData.IdentityIssuer!, cancellationToken));
        }
    });

using var host = builder.Build();
await host.StartAsync();

try
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));

    await using var scope = host.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    if (scope.ServiceProvider.GetRequiredService<IOptions<LabDataOptions>>().Value.Reset)
    {
        await LabDataReset.ResetAsync(context, timeout.Token);
    }

    await context.Database.MigrateAsync(timeout.Token);
}
finally
{
    await host.StopAsync();
}

return 0;
