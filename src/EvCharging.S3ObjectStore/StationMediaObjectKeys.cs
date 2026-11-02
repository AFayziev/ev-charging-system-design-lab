namespace EvCharging.S3ObjectStore;

/// <summary>
/// Station media object keys, derived from public identifiers. A browser upload lands under
/// <see cref="ObjectStore.UploadPrefix"/> and is processed from there; published variants share one prefix per
/// photo, so removing the photo removes them together.
/// </summary>
public static class StationMediaObjectKeys
{
    public const string ThumbnailVariant = "thumbnail";
    public const string DisplayVariant = "display";

    public static IReadOnlyList<string> Variants { get; } = [ThumbnailVariant, DisplayVariant];

    public static string Upload(Guid stationId, Guid itemId) =>
        $"{ObjectStore.UploadPrefix}{Prefix(stationId, itemId)}";

    /// <summary>The published video, stored as uploaded under the same prefix as a photo's variants.</summary>
    public static string Video(Guid stationId, Guid itemId) => $"{Prefix(stationId, itemId)}/video.mp4";

    public static string Variant(Guid stationId, Guid itemId, string variant) =>
        $"{Prefix(stationId, itemId)}/{variant}.webp";

    private static string Prefix(Guid stationId, Guid itemId) => $"stations/{stationId:D}/media/{itemId:D}";

    /// <summary>The prefix that lists exactly this photo's published variants.</summary>
    public static string VariantPrefix(Guid stationId, Guid itemId) => $"{Prefix(stationId, itemId)}/";
}
