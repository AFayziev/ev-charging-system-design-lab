using EvCharging.Domain.Media;
using EvCharging.MediaWorker.Configuration;
using FluentResults;
using Microsoft.Extensions.Options;
using TagLib;
using TagLib.Mpeg4;

namespace EvCharging.MediaWorker.Processing;

/// <summary>
/// Decides from its header boxes whether an uploaded video can be published as it is: an MP4 file within the
/// duration limit whose video plays as H.264 and whose sound, if any, is MPEG-4 audio, which every browser plays.
/// Nothing is decoded or rewritten, so the published file keeps whatever metadata the operator's file carried.
/// </summary>
public sealed class StationVideoVerifier(IOptions<StationMediaLimitsOptions> limits)
{
    private const string Mp4ContentType = "video/mp4";

    // The MPEG-4 audio sample entry, shared by AAC and by MP3 in MP4; both play in browsers.
    private const string Mpeg4AudioFormat = "mp4a";

    public Result Verify(Stream upload, string declaredContentType)
    {
        // An ISO media file starts with its file type box. TagLib reports anything else as corrupt, which would
        // blur a file of another format with a damaged MP4.
        if (declaredContentType != Mp4ContentType || !StartsWithFileTypeBox(upload))
        {
            return Reject(StationMediaFailure.UnsupportedFormat);
        }

        using var file = OpenFile(upload);
        if (file is null)
        {
            return Reject(StationMediaFailure.Undecodable);
        }

        if (file.Properties.Duration > limits.Value.MaximumVideoDuration)
        {
            return Reject(StationMediaFailure.TooLong);
        }

        // The codec list has an empty entry for a missing kind of track, such as the sound of a silent video.
        // Other tracks are left alone, as they do not affect playback.
        bool hasVideo = false;
        bool playable = true;
        foreach (var codec in file.Properties.Codecs.OfType<Box>())
        {
            string format = codec.BoxType.ToString();
            switch (codec)
            {
                case IsoVisualSampleEntry:
                    hasVideo = true;
                    playable &= format is "avc1" or "avc3";
                    break;
                case IsoAudioSampleEntry:
                    playable &= format == Mpeg4AudioFormat;
                    break;
            }
        }

        // A file without a video track is some other kind of media, such as an audio file or a still image.
        return !hasVideo
            ? Reject(StationMediaFailure.UnsupportedFormat)
            : playable ? Result.Ok() : Reject(StationMediaFailure.UnsupportedCodec);
    }

    private static bool StartsWithFileTypeBox(Stream upload)
    {
        Span<byte> header = stackalloc byte[8];
        upload.Position = 0;
        return upload.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header[4..].SequenceEqual("ftyp"u8);
    }

    // TagLib reads the file lazily from the stream, so the stream must stay open while the file is used.
    private static TagLib.File? OpenFile(Stream upload)
    {
        upload.Position = 0;
        try
        {
            return TagLib.File.Create(new UploadFileAbstraction(upload), Mp4ContentType, ReadStyle.Average);
        }
        catch (CorruptFileException)
        {
            return null;
        }
    }

    private static Result Reject(string failureCode) => Result.Fail(new StationMediaRejectedError(failureCode));
}
