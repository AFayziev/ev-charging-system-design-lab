using EvCharging.MediaWorker.Processing;
using Wolverine;

namespace EvCharging.MediaWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<MediaProcessingOptions>()
            .BindConfiguration(MediaProcessingOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.RetryDelays.All(delay => delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1)),
                "Configure retry delays of at most one hour.")
            .Validate(
                options => options.MissingUploadRetryDelay > TimeSpan.Zero
                    && options.MissingUploadRetryDelay <= TimeSpan.FromMinutes(5),
                "Configure a missing-upload retry delay of at most five minutes.")
            .ValidateOnStart();
        services.AddOptions<StationMediaLimitsOptions>()
            .BindConfiguration(StationMediaLimitsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<StationPhotoProcessor>();
        services.AddSingleton<IWolverineExtension, MediaMessagingExtension>();
        return services;
    }
}
