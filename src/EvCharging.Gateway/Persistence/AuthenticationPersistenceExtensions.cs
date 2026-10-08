using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EvCharging.Gateway.Persistence;

public static class AuthenticationPersistenceExtensions
{
    public static IHostApplicationBuilder AddAuthenticationPersistence(this IHostApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("gateway-auth");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("The gateway-auth connection is required.");
        }

        builder.Services.AddOptions<AuthenticationDatabaseOptions>()
            .Bind(builder.Configuration.GetSection("AuthenticationDatabase"))
            .ValidateDataAnnotations().ValidateOnStart();

        builder.Services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<AuthenticationDatabaseOptions>>().Value;
            var connectionSettings = new NpgsqlConnectionStringBuilder(connection)
            {
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = settings.MaximumPoolSize,
                Timeout = settings.AcquisitionTimeoutSeconds,
                CommandTimeout = settings.CommandTimeoutSeconds,
                IncludeErrorDetail = false,
                ApplicationName = "gateway-auth"
            };
            return new NpgsqlDataSourceBuilder(connectionSettings.ConnectionString) { Name = "gateway-auth-primary" }.Build();
        });

        builder.Services.AddDbContext<AuthenticationDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "gateway_auth")));

        // A shared, named datasource makes the pool budget independent of EF context lifetime.
        // Ambiguous session writes must be reconciled by their caller, never replayed implicitly.
        builder.EnrichNpgsqlDbContext<AuthenticationDbContext>(settings => settings.DisableRetry = true);

        return builder;
    }
}
