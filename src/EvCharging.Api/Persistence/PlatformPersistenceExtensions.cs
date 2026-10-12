using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EvCharging.Api.Persistence;

internal static class PlatformPersistenceExtensions
{
    public const string ReadRoute = "platform-read";
    public const string WriteRoute = "platform-write";

    public static IHostApplicationBuilder AddPlatformPersistence(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAuditActorAccessor, HttpAuditActorAccessor>();
        builder.Services.AddScoped<AuditEntryWriter>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        foreach (string route in new[] { ReadRoute, WriteRoute })
        {
            string? connection = builder.Configuration.GetConnectionString(route);
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException($"The {route} connection is required.");
            }

            builder.Services.AddKeyedSingleton(route, (_, _) => NpgsqlDataSource.Create(connection));
        }

        builder.Services.AddDbContext<PlatformReadDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredKeyedService<NpgsqlDataSource>(ReadRoute)));
        builder.EnrichNpgsqlDbContext<PlatformReadDbContext>(settings => settings.DisableRetry = true);

        builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredKeyedService<NpgsqlDataSource>(WriteRoute))
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
        builder.EnrichNpgsqlDbContext<PlatformDbContext>(settings => settings.DisableRetry = true);
        return builder;
    }
}
