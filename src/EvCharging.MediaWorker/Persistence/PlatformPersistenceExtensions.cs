using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.MediaWorker.Persistence;

internal static class PlatformPersistenceExtensions
{
    public static IHostApplicationBuilder AddPlatformPersistence(this IHostApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("messaging");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("The messaging connection is required.");
        }

        builder.Services.AddScoped<IAuditActorAccessor, WorkerAuditActorAccessor>();
        builder.Services.AddScoped<AuditEntryWriter>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<PlatformDbContext>(
            (services, options) => options.UseNpgsql(connection)
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
        builder.EnrichNpgsqlDbContext<PlatformDbContext>(settings => settings.DisableRetry = true);
        return builder;
    }
}
