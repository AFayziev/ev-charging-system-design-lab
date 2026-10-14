using EvCharging.NotificationWorker.Templates.Reservation;
using Wolverine;

namespace EvCharging.NotificationWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ReservationEmailRenderer>();
        services.AddOptions<MessagingFailureOptions>()
            .BindConfiguration("MessagingFailures")
            .Validate(
                options => options.TransientDatabaseRetryDelays is { Length: > 0 and <= 5 }
                    && options.TransientDatabaseRetryDelays.All(delay => delay > TimeSpan.Zero
                        && delay <= TimeSpan.FromMinutes(1)),
                "Configure one to five positive database retry delays, each at most one minute.")
            .ValidateOnStart();
        services.AddOptions<NotificationPreparationOptions>()
            .BindConfiguration("NotificationPreparation")
            .Validate(
                options => options.MaximumConcurrentPreparations is > 0 and <= 32,
                "Concurrent notification preparations must be 1 to 32.")
            .ValidateOnStart();
        services.AddSingleton<IWolverineExtension, NotificationMessagingExtension>();
        return services;
    }
}
