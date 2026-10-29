using EvCharging.Domain.Media;
using EvCharging.MediaWorker.Configuration;
using EvCharging.S3ObjectStore;
using FluentResults;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace EvCharging.MediaWorker.Processing;

/// <summary>
/// Turns an uploaded photo into the WebP variants that are published. Re-encoding leaves out the original's
/// metadata, such as its location, and the original itself is never published.
/// </summary>
public sealed class StationPhotoProcessor(
    IOptions<MediaProcessingOptions> processing, IOptions<StationMediaLimitsOptions> limits)
{
    private static readonly Dictionary<string, SKEncodedImageFormat> DeclaredFormats = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = SKEncodedImageFormat.Jpeg,
        ["image/png"] = SKEncodedImageFormat.Png,
        ["image/webp"] = SKEncodedImageFormat.Webp
    };

    public Result<IReadOnlyList<StationPhotoVariant>> Process(byte[] upload, string declaredContentType)
    {
        // The header says what the file is and how large it decodes, so both are checked before any pixels are
        // allocated; a small file can still describe an enormous image.
        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return Reject(StationMediaFailure.Undecodable);
        }

        if (!DeclaredFormats.TryGetValue(declaredContentType, out var declared) || codec.EncodedFormat != declared)
        {
            return Reject(StationMediaFailure.UnsupportedFormat);
        }

        if ((long)codec.Info.Width * codec.Info.Height > limits.Value.MaximumPhotoPixels)
        {
            return Reject(StationMediaFailure.TooManyPixels);
        }

        using var source = SKBitmap.Decode(codec);
        if (source is null)
        {
            return Reject(StationMediaFailure.Undecodable);
        }

        // The display image is made first, at its final size, so the thumbnail is made from a small bitmap rather
        // than from the full-size source. Photos are published as uploaded: the operator uploads them upright.
        var settings = processing.Value;
        using var display = ScaledToFit(source, settings.DisplayEdge);
        using var thumbnail = display is null ? null : ScaledToFit(display, settings.ThumbnailEdge);
        if (display is null || thumbnail is null)
        {
            return Reject(StationMediaFailure.Undecodable);
        }

        var thumbnailVariant = Encode(thumbnail, StationMediaObjectKeys.ThumbnailVariant);
        var displayVariant = Encode(display, StationMediaObjectKeys.DisplayVariant);
        return thumbnailVariant is null || displayVariant is null
            ? Reject(StationMediaFailure.Undecodable)
            : Result.Ok<IReadOnlyList<StationPhotoVariant>>([thumbnailVariant, displayVariant]);
    }

    public static Result<IReadOnlyList<StationPhotoVariant>> Reject(string failureCode) =>
        Result.Fail<IReadOnlyList<StationPhotoVariant>>(new StationPhotoRejectedError(failureCode));

    // Photos are only made smaller, never enlarged. Skia reports a bitmap it cannot produce as null.
    private static SKBitmap? ScaledToFit(SKBitmap bitmap, int longestEdge)
    {
        double scale = Math.Min(1d, (double)longestEdge / Math.Max(bitmap.Width, bitmap.Height));
        var size = new SKSizeI(
            Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
        return bitmap.Resize(size, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private StationPhotoVariant? Encode(SKBitmap bitmap, string name)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image?.Encode(SKEncodedImageFormat.Webp, processing.Value.WebpQuality);
        return encoded is null ? null : new StationPhotoVariant(name, encoded.ToArray());
    }
}
