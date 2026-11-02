using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EvCharging.S3ObjectStore;

/// <summary>The public delivery address of published media; hosts that only link to media need nothing else.</summary>
public sealed class PublicMediaOptions
{
    [Required, Url]
    public string PublicBaseUrl { get; set; } = "";
}

/// <summary>Builds the public address of a published photo variant from the configured base URL.</summary>
public sealed class StationMediaUrls(IOptions<PublicMediaOptions> options)
{
    private readonly string _baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');

    public Uri Thumbnail(Guid stationId, Guid itemId) =>
        Variant(stationId, itemId, StationMediaObjectKeys.ThumbnailVariant);

    public Uri Display(Guid stationId, Guid itemId) =>
        Variant(stationId, itemId, StationMediaObjectKeys.DisplayVariant);

    private Uri Variant(Guid stationId, Guid itemId, string variant)
    {
        var key = StationMediaObjectKeys.Variant(stationId, itemId, variant);
        return new Uri($"{_baseUrl}/{key}");
    }
}

public static class StationMediaUrlsExtensions
{
    public static IHostApplicationBuilder AddStationMediaUrls(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<PublicMediaOptions>()
            .BindConfiguration(ObjectStoreOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.TryAddSingleton<StationMediaUrls>();
        return builder;
    }
}
