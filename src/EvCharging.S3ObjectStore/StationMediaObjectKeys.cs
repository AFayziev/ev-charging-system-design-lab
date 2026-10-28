namespace EvCharging.S3ObjectStore;

/// <summary>
/// Station media object keys, derived from public identifiers. Browser uploads 
/// land under <see cref="ObjectStore.UploadPrefix"/>; verified originals move out of it before lifecycle expiry.
/// </summary>
public static class StationMediaObjectKeys
{
    public static string Upload(Guid stationId, Guid itemId, Guid versionId) =>
        $"{ObjectStore.UploadPrefix}{Path(stationId, itemId, versionId)}";

    public static string Original(Guid stationId, Guid itemId, Guid versionId) =>
        $"originals/{Path(stationId, itemId, versionId)}";

    private static string Path(Guid stationId, Guid itemId, Guid versionId) =>
        $"stations/{stationId:D}/media/{itemId:D}/{versionId:D}";
}
