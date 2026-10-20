using EvCharging.ChargingWorker.Scheduling;

namespace EvCharging.ChargingWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<ChargingExecutionOptions>()
            .BindConfiguration("ChargingExecution")
            .ValidateDataAnnotations()
            .Validate(
                options => options.AutomaticStatusCheckInterval > TimeSpan.Zero
                    && options.AutomaticStatusCheckInterval <= TimeSpan.FromMinutes(1),
                "The automatic check interval must be positive and at most one minute.")
            .ValidateOnStart();
        services.AddOptions<MessagingFailureOptions>()
            .BindConfiguration("MessagingFailures")
            .Validate(
                options => options.TransientDatabaseRetryDelays.Length is > 0 and <= 5
                    && options.TransientDatabaseRetryDelays.All(delay => delay > TimeSpan.Zero
                        && delay <= TimeSpan.FromMinutes(1)),
                "Configure one to five positive database retry delays, each at most one minute.")
            .ValidateOnStart();
        services.AddOptions<ReservationExpiryOptions>()
            .BindConfiguration("ReservationExpiry")
            .Validate(options => options.SweepInterval > TimeSpan.Zero, "The expiry sweep interval must be positive.")
            .Validate(options => options.SweepBatchSize is > 0 and <= 1000, "The expiry sweep batch must be 1 to 1000.")
            .Validate(
                options => options.MaximumConcurrentChecks is > 0 and <= 32,
                "Concurrent expiry checks must be 1 to 32.")
            .ValidateOnStart();
        services.AddHostedService<OverdueReservationSweep>();
        return services;
    }
}
