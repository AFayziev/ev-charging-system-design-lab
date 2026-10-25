using EvCharging.SearchIndexWorker.Indexing;
using Wolverine;

namespace EvCharging.SearchIndexWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<SearchIndexingOptions>()
            .BindConfiguration(SearchIndexingOptions.SectionName)
            .Validate(
                options => options.RetryDelays is { Length: > 0 and <= 5 }
                    && options.RetryDelays.All(delay => delay > TimeSpan.Zero && delay <= TimeSpan.FromMinutes(5)),
                "Configure one to five positive retry delays, each at most five minutes.")
            .ValidateOnStart();
        services.AddSingleton<StationSearchIndexWriter>();
        services.AddSingleton<IWolverineExtension, SearchMessagingExtension>();
        return services;
    }
}
