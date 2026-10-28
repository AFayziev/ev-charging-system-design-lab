using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using Xunit;

namespace EvCharging.Domain.Tests.Media;

public sealed class StationMediaVersionTests
{
    private const string Jpeg = "image/jpeg";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly StationMediaLimits Limits = new(
        5, 1000, new HashSet<string>(StringComparer.Ordinal) { Jpeg }, TimeSpan.FromMinutes(30));

    [Fact]
    public void CompleteUpload_MatchingObject_RecordsTheUpload()
    {
        var version = StartUpload();

        var result = version.CompleteUpload(1000, Jpeg, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(StationMediaVersionState.Uploaded, version.State);
        Assert.Equal(Now.AddMinutes(1), version.UploadedAt);
    }

    [Theory]
    [InlineData(999, Jpeg)]
    [InlineData(1000, "image/png")]
    [InlineData(1000, null)]
    public void CompleteUpload_DifferentObject_FailsTheUpload(long length, string? contentType)
    {
        var version = StartUpload();

        var result = version.CompleteUpload(length, contentType, Now);

        Assert.IsType<StationMediaUploadFailedError>(Assert.Single(result.Errors));
        Assert.Equal(StationMediaVersionState.Failed, version.State);
        Assert.Null(version.UploadedAt);
    }

    [Fact]
    public void CompleteUpload_AlreadyUploaded_KeepsTheFirstCompletion()
    {
        var version = StartUpload();
        version.CompleteUpload(1000, Jpeg, Now);

        var repeated = version.CompleteUpload(1, "image/png", Now.AddMinutes(5));

        Assert.True(repeated.IsSuccess);
        Assert.Equal(Now, version.UploadedAt);
    }

    [Fact]
    public void CompleteUpload_AlreadyFailed_StaysFailed()
    {
        var version = StartUpload();
        version.CompleteUpload(1, Jpeg, Now);

        var repeated = version.CompleteUpload(1000, Jpeg, Now);

        Assert.IsType<StationMediaUploadFailedError>(Assert.Single(repeated.Errors));
        Assert.Equal(StationMediaVersionState.Failed, version.State);
    }

    [Fact]
    public void RecordMissingUpload_BeforeExpiry_KeepsAwaitingTheUpload()
    {
        var version = StartUpload();

        var result = version.RecordMissingUpload(version.UploadExpiresAt.AddTicks(-1));

        Assert.IsType<StationMediaUploadMissingError>(Assert.Single(result.Errors));
        Assert.Equal(StationMediaVersionState.AwaitingUpload, version.State);
    }

    [Fact]
    public void RecordMissingUpload_AtExpiry_FailsTheUpload()
    {
        var version = StartUpload();

        var result = version.RecordMissingUpload(version.UploadExpiresAt);

        Assert.IsType<StationMediaUploadFailedError>(Assert.Single(result.Errors));
        Assert.Equal(StationMediaVersionState.Failed, version.State);
    }

    private static StationMediaVersion StartUpload()
    {
        var station = new Station
        {
            Name = "Station",
            Address = "Address",
            TimeZoneId = "Asia/Tashkent",
            AccessInstructions = ""
        };
        return StationMediaItem.StartPhotoUpload(station, 0, 0, Jpeg, 1000, Limits, Now).Value.Versions.Single();
    }
}
