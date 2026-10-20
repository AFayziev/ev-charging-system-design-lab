using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;

namespace EvCharging.WattHarborProviderApi.Persistence;

public static class ProviderPersistenceExtensions
{
    public static IHostApplicationBuilder AddProviderPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<ProviderDbContext>(
            "wattharbor",
            settings => settings.DisableRetry = true,
            configureDbContextOptions: options => options.UseProviderModel());
        return builder;
    }

    public static DbContextOptionsBuilder UseProviderModel(this DbContextOptionsBuilder options) =>
        options.UseSnakeCaseNamingConvention()
            .UseNpgsql(postgres => postgres
                .MigrationsAssembly("EvCharging.WattHarborProviderApi.Migrations")
                .MapEnum<CommandKind>("command_kind", nameTranslator: new NpgsqlSnakeCaseNameTranslator()))
            .UseSeeding((context, _) => ProviderSeedData.Seed(context))
            .UseAsyncSeeding((context, _, cancellationToken) => ProviderSeedData.SeedAsync(context, cancellationToken));
}
