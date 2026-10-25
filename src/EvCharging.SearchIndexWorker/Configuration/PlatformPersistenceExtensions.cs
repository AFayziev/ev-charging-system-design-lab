using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Configuration;

internal static class PlatformPersistenceExtensions
{
    public static IHostApplicationBuilder AddPlatformPersistence(this IHostApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("messaging");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("The messaging connection is required.");
        }

        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connection));
        builder.EnrichNpgsqlDbContext<PlatformDbContext>(settings => settings.DisableRetry = true);
        return builder;
    }
}
