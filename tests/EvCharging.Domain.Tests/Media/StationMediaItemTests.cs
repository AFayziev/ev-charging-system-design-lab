using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using Xunit;

namespace EvCharging.Domain.Tests.Media;

public sealed class StationMediaItemTests
{
    private const string Jpeg = "image/jpeg";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly StationMediaLimits Limits = new(
        2, 1000, new HashSet<string>(StringComparer.Ordinal) { Jpeg, "image/png" }, TimeSpan.FromMinutes(30));

    [Fact]
    public void StartPhotoUpload_WithinLimits_AwaitsTheUploadAtTheNextPosition()
    {
        var station = CreateStation();

        var result = StationMediaItem.StartPhotoUpload(
            station, 1, 3, Jpeg, 1000, Limits, Now.ToOffset(TimeSpan.FromHours(5)));

        Assert.True(result.IsSuccess);
        var item = result.Value;
        Assert.Equal((StationMediaKind.Photo, 3, Now), (item.Kind, item.Position, item.CreatedAt));
        Assert.NotEqual(Guid.Empty, item.PublicId);
        var version = Assert.Single(item.Versions);
        Assert.Equal(StationMediaVersionState.AwaitingUpload, version.State);
        Assert.Equal((Jpeg, 1000L), (version.ContentType, version.ByteLength));
        Assert.Equal(Now.AddMinutes(30), version.UploadExpiresAt);
        Assert.NotEqual(item.PublicId, version.PublicId);
        Assert.Equal(1, station.GalleryVersion);
    }

    [Fact]
    public void StartPhotoUpload_GalleryFull_RejectsWithoutAdvancingTheGalleryVersion()
    {
        var station = CreateStation();

        var result = StationMediaItem.StartPhotoUpload(station, 2, 2, Jpeg, 1000, Limits, Now);

        Assert.IsType<StationMediaLimitError>(Assert.Single(result.Errors));
        Assert.Equal(0, station.GalleryVersion);
    }

    [Theory]
    [InlineData("image/gif", 1000, StationMediaUploadField.ContentType)]
    [InlineData("IMAGE/JPEG", 1000, StationMediaUploadField.ContentType)]
    [InlineData(Jpeg, 0, StationMediaUploadField.ByteLength)]
    [InlineData(Jpeg, 1001, StationMediaUploadField.ByteLength)]
    public void StartPhotoUpload_OutsideFileLimits_RejectsTheField(
        string contentType, long byteLength, StationMediaUploadField field)
    {
        var result = StationMediaItem.StartPhotoUpload(CreateStation(), 0, 0, contentType, byteLength, Limits, Now);

        Assert.Equal(field, Assert.IsType<StationMediaUploadValidationError>(Assert.Single(result.Errors)).Field);
    }

    private static Station CreateStation() => new()
    {
        Id = Guid.NewGuid(),
        PublicId = Guid.NewGuid(),
        Name = "Station",
        Address = "Address",
        TimeZoneId = "Asia/Tashkent",
        AccessInstructions = ""
    };
}
