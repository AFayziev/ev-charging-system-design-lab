using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Persistence;

internal static class PlatformPersistenceExtensions
{
    public static IHostApplicationBuilder AddPlatformPersistence(this IHostApplicationBuilder builder)
    {
        string connection = builder.Configuration.GetConnectionString("platform-read")
            ?? throw new InvalidOperationException("The platform-read connection is required.");

        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connection));
        builder.EnrichNpgsqlDbContext<PlatformDbContext>(settings => settings.DisableRetry = true);
        return builder;
    }
}
