using EvCharging.SearchApi.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EvCharging.SearchApi.Features.Stations;

internal static class StationSearchResilience
{
    public const string PipelineKey = "station-search";

    /// <summary>A search only reads, so a transient failure of the index is retried briefly before it is reported.</summary>
    public static IServiceCollection AddStationSearchResilience(this IServiceCollection services) =>
        services.AddResiliencePipeline(
            PipelineKey,
            (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<SearchApiOptions>>().Value;
                builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryAttempts,
                    Delay = options.RetryDelay,
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder().Handle<SearchUnavailableException>()
                });
            });
}
