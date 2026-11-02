namespace EvCharging.MediaWorker.Tests;

/// <summary>
/// Small real videos made once with FFmpeg and checked in. FFmpeg is not needed to build or run the tests.
/// </summary>
internal static class VideoFiles
{
    public const string Mp4 = "video/mp4";

    /// <summary>Two seconds of H.264 with AAC sound.</summary>
    public static byte[] H264Aac => Read("h264-aac.mp4");

    /// <summary>Two seconds of H.264 without sound.</summary>
    public static byte[] H264Silent => Read("h264-silent.mp4");

    /// <summary>Sixty-one seconds of H.264, one second over the limit.</summary>
    public static byte[] H264TooLong => Read("h264-long.mp4");

    /// <summary>Two seconds of HEVC, which most browsers do not play.</summary>
    public static byte[] Hevc => Read("hevc.mp4");

    /// <summary>H.264 with Opus sound, which is not AAC.</summary>
    public static byte[] H264Opus => Read("h264-opus.mp4");

    /// <summary>A WebM file, which is not an MP4 container.</summary>
    public static byte[] Webm => Read("vp8.webm");

    /// <summary>A valid video cut off before its movie box.</summary>
    public static byte[] Truncated => H264Aac[..200];

    public static byte[] Read(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Videos", name));
}
