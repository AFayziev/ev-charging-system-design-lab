using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EvCharging.ObservationStream;

internal static class ObservationResilience
{
    public const string PipelineKey = "observation-stream";

    /// <summary>Retries stream reads and store writes with exponential backoff until they succeed.</summary>
    public static IServiceCollection AddObservationResilience(this IServiceCollection services) =>
        services.AddResiliencePipeline(
            PipelineKey,
            (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<ObservationProcessingOptions>>()
                    .Value;
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("EvCharging.ObservationStream.Resilience");
                builder.AddRetry(new RetryStrategyOptions
                {
                    // The stream retains the readings, so an outage only delays the views.
                    MaxRetryAttempts = int.MaxValue,
                    Delay = options.RetryDelay,
                    MaxDelay = options.MaximumRetryDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(IsRetryable),
                    OnRetry = arguments =>
                    {
                        logger.LogWarning(
                            arguments.Outcome.Exception,
                            "Attempt {Attempt} failed; the next is in {Delay}.",
                            arguments.AttemptNumber + 1,
                            arguments.RetryDelay);
                        return default;
                    }
                });
            });

    private static bool IsRetryable(Exception exception) =>
        exception is not OperationCanceledException and not ConsumeException { Error.IsFatal: true };
}
