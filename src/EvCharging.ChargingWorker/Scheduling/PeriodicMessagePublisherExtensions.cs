using Microsoft.Extensions.Options;

namespace EvCharging.ChargingWorker.Scheduling;

internal static class PeriodicMessagePublisherExtensions
{
    /// <summary>Sends <typeparamref name="TMessage"/> at startup and then on the interval read from its options.</summary>
    public static IServiceCollection AddPeriodicMessage<TMessage, TOptions>(
        this IServiceCollection services, Func<TOptions, TimeSpan> interval)
        where TMessage : new()
        where TOptions : class =>
        services.AddHostedService(provider => new PeriodicMessagePublisher<TMessage>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            interval(provider.GetRequiredService<IOptions<TOptions>>().Value),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PeriodicMessagePublisher<TMessage>>>()));
}
