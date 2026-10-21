using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Features.Observations;

internal sealed class ObservationIngestionOptions
{
    public const string SectionName = "ObservationIngestion";

    /// <summary>The most observations one callback may carry.</summary>
    [Range(1, 1000)]
    public int MaximumBatchSize { get; init; } = 100;
}

internal static class ObservationIngestionExtensions
{
    public static IHostApplicationBuilder AddObservationIngestion(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObservationIngestionOptions>()
            .BindConfiguration(ObservationIngestionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return builder;
    }
}
