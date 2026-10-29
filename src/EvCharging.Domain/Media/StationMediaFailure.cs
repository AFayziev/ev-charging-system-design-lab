namespace EvCharging.Domain.Media;

/// <summary>Why a photo could not be published. The code is shown to the operator.</summary>
public static class StationMediaFailure
{
    public const string UploadMissing = "upload_missing";
    public const string UploadExpired = "upload_expired";
    public const string Undecodable = "undecodable";
    public const string TooManyPixels = "too_many_pixels";
    public const string UnsupportedFormat = "unsupported_format";

    public static readonly IReadOnlyList<string> Codes =
        [UploadMissing, UploadExpired, Undecodable, TooManyPixels, UnsupportedFormat];
}
