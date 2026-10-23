namespace EvCharging.ChargingWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<ChargingExecutionOptions>()
            .BindConfiguration(ChargingExecutionOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.AutomaticStatusCheckInterval > TimeSpan.Zero
                    && options.AutomaticStatusCheckInterval <= TimeSpan.FromMinutes(1),
                "The automatic check interval must be positive and at most one minute.")
            .Validate(
                options => options.ContinuedStatusCheckInterval > TimeSpan.Zero
                    && options.ContinuedStatusCheckInterval <= TimeSpan.FromMinutes(5),
                "The continued check interval must be positive and at most five minutes.")
            .Validate(
                options => options.MaximumContinuedStatusCheckInterval >= options.ContinuedStatusCheckInterval
                    && options.MaximumContinuedStatusCheckInterval <= TimeSpan.FromHours(1),
                "The maximum continued check interval must be from the continued interval up to one hour.")
            .ValidateOnStart();
        services.AddOptions<MessagingFailureOptions>()
            .BindConfiguration(MessagingFailureOptions.SectionName)
            .Validate(
                options => options.TransientDatabaseRetryDelays.Length is > 0 and <= 5
                    && options.TransientDatabaseRetryDelays.All(delay => delay > TimeSpan.Zero
                        && delay <= TimeSpan.FromMinutes(1)),
                "Configure one to five positive database retry delays, each at most one minute.")
            .ValidateOnStart();
        services.AddOptions<ReservationExpiryOptions>()
            .BindConfiguration(ReservationExpiryOptions.SectionName)
            .Validate(
                options => options.BatchSize is > 0 and <= 1000,
                "The overdue reservation batch must be 1 to 1000.")
            .Validate(
                options => options.MaximumConcurrentChecks is > 0 and <= 32,
                "Concurrent expiry checks must be 1 to 32.")
            .ValidateOnStart();
        return services;
    }
}
