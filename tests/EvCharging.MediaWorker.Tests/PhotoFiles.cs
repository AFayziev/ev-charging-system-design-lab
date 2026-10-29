using SkiaSharp;

namespace EvCharging.MediaWorker.Tests;

internal static class PhotoFiles
{
    // A 1x1 GIF, which Skia decodes but the gallery does not accept.
    public static byte[] Gif { get; }
        = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    // A JPEG with an EXIF block, as a phone leaves in its photos; the block's orientation value is 6.
    public static byte[] JpegWithExif(int width, int height)
    {
        var jpeg = Encode(width, height, SKEncodedImageFormat.Jpeg);
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    public static PhotoFacts Inspect(byte[] file)
    {
        using var data = SKData.CreateCopy(file);
        using var codec = SKCodec.Create(data)!;
        return new PhotoFacts(codec.Info.Width, codec.Info.Height, codec.EncodedFormat, codec.EncodedOrigin);
    }
}

internal sealed record PhotoFacts(int Width, int Height, SKEncodedImageFormat Format, SKEncodedOrigin Origin);
