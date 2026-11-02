using EvCharging.Domain.Media;
using EvCharging.MediaWorker.Configuration;
using EvCharging.MediaWorker.Processing;
using FluentResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace EvCharging.MediaWorker.Tests;

// The rules this worker applies to a video, over small real files. How the MP4 header is read is the library's
// concern and is not tested here.
public sealed class StationVideoVerifierTests
{
    private readonly StationVideoVerifier _verifier = new(Options.Create(
        new StationMediaLimitsOptions { MaximumPhotoPixels = 1, MaximumVideoDuration = TimeSpan.FromSeconds(60) }));

    [Theory]
    [InlineData("h264-aac.mp4")]
    [InlineData("h264-silent.mp4")]
    public void Verify_H264WithAacOrNoSound_Passes(string file) =>
        Assert.True(Verify(VideoFiles.Read(file)).IsSuccess);

    [Theory]
    [InlineData("h264-long.mp4", StationMediaFailure.TooLong)]
    [InlineData("hevc.mp4", StationMediaFailure.UnsupportedCodec)]
    [InlineData("h264-opus.mp4", StationMediaFailure.UnsupportedCodec)]
    [InlineData("vp8.webm", StationMediaFailure.UnsupportedFormat)]
    public void Verify_UnacceptableVideo_IsRejectedWithItsReason(string file, string failureCode) =>
        AssertRejected(failureCode, VideoFiles.Read(file));

    [Fact]
    public void Verify_NotAnMp4Type_IsAnUnsupportedFormat() =>
        AssertRejected(StationMediaFailure.UnsupportedFormat, VideoFiles.H264Aac, "video/webm");

    [Fact]
    public void Verify_FileWithoutAnMp4Header_IsAnUnsupportedFormat() =>
        AssertRejected(StationMediaFailure.UnsupportedFormat, new byte[500]);

    [Fact]
    public void Verify_FileCutOffBeforeItsMovieBox_IsUndecodable() =>
        AssertRejected(StationMediaFailure.Undecodable, VideoFiles.Truncated);

    private Result Verify(byte[] file, string contentType = VideoFiles.Mp4)
    {
        using var stream = new MemoryStream(file);
        return _verifier.Verify(stream, contentType);
    }

    private void AssertRejected(string failureCode, byte[] file, string contentType = VideoFiles.Mp4)
    {
        var result = Verify(file, contentType);

        Assert.Equal(failureCode, Assert.IsType<StationMediaRejectedError>(Assert.Single(result.Errors)).FailureCode);
    }
}
