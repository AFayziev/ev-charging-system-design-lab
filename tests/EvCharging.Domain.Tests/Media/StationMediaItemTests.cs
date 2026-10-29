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
        Assert.Equal((StationMediaKind.Photo, 3, StationMediaState.Uploading), (item.Kind, item.Position, item.State));
        Assert.Equal((Jpeg, 1000L, Now), (item.ContentType, item.ByteLength, item.CreatedAt));
        Assert.Equal(Now.AddMinutes(30), item.UploadExpiresAt);
        Assert.NotEqual(Guid.Empty, item.PublicId);
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

    [Fact]
    public void CompleteUpload_UploadingPhoto_MovesToProcessingAndRepeatsChangeNothing()
    {
        var item = StartUpload();

        Assert.True(item.CompleteUpload().IsSuccess);
        Assert.True(item.CompleteUpload().IsSuccess);

        Assert.Equal(StationMediaState.Processing, item.State);
    }

    [Fact]
    public void CompleteUpload_FailedPhoto_ReportsTheFailure()
    {
        var item = StartUpload();
        item.ResolveExpiredUpload(fileExists: false);

        var result = item.CompleteUpload();

        Assert.IsType<StationMediaUploadFailedError>(Assert.Single(result.Errors));
        Assert.Equal(StationMediaState.Failed, item.State);
    }

    [Theory]
    [InlineData(true, StationMediaState.Processing, null)]
    [InlineData(false, StationMediaState.Failed, StationMediaFailure.UploadExpired)]
    public void ResolveExpiredUpload_UnfinishedUpload_ProcessesAnArrivedFileAndFailsOtherwise(
        bool fileExists, StationMediaState expected, string? failureCode)
    {
        var item = StartUpload();

        var needsProcessing = item.ResolveExpiredUpload(fileExists);

        Assert.Equal(fileExists, needsProcessing);
        Assert.Equal((expected, failureCode), (item.State, item.FailureCode));
    }

    [Fact]
    public void ResolveExpiredUpload_PhotoAlreadyCompleted_LeavesItAsItIs()
    {
        var item = StartUpload();
        item.CompleteUpload();

        var needsProcessing = item.ResolveExpiredUpload(fileExists: false);

        Assert.False(needsProcessing);
        Assert.Equal(StationMediaState.Processing, item.State);
    }

    [Fact]
    public void Fail_ProcessingPhoto_RecordsTheCode()
    {
        var item = StartProcessing();

        var result = item.Fail(StationMediaFailure.Undecodable);

        Assert.True(result.IsSuccess);
        Assert.Equal((StationMediaState.Failed, StationMediaFailure.Undecodable), (item.State, item.FailureCode));
    }

    [Fact]
    public void Fail_PublishedPhoto_IsRejectedAndKeepsItsState()
    {
        var item = Publish(StartProcessing());

        var result = item.Fail(StationMediaFailure.Undecodable);

        Assert.IsType<StationMediaStateError>(Assert.Single(result.Errors));
        Assert.Equal((StationMediaState.Published, null), (item.State, item.FailureCode));
    }

    [Fact]
    public void Publish_ProcessingPhoto_RecordsThePublication()
    {
        var item = StartProcessing();

        var result = item.Publish(null, Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal((StationMediaState.Published, Now.AddMinutes(2)), (item.State, item.PublishedAt));
    }

    [Fact]
    public void Publish_PhotoThatIsNotProcessing_IsRejected()
    {
        var item = StartUpload();

        var result = item.Publish(null, Now);

        Assert.IsType<StationMediaStateError>(Assert.Single(result.Errors));
        Assert.Equal((StationMediaState.Uploading, null), (item.State, item.PublishedAt));
    }

    [Fact]
    public void StartPhotoReplacement_PublishedPhoto_WaitsWithoutAPositionAndDoesNotCountTowardTheLimit()
    {
        var station = CreateStation();
        var published = Publish(StartProcessing(position: 1));

        var result = StationMediaItem.StartPhotoReplacement(station, published, Jpeg, 500, Limits, Now);

        Assert.True(result.IsSuccess);
        var replacement = result.Value;
        Assert.Equal(
            (published.Id, null, StationMediaState.Uploading),
            (
                replacement.ReplacesItemId, replacement.Position, replacement.State));
        Assert.Equal(1, station.GalleryVersion);
    }

    [Fact]
    public void StartPhotoReplacement_PhotoThatIsNotPublished_IsRejectedWithoutAdvancingTheGalleryVersion()
    {
        var station = CreateStation();

        var result = StationMediaItem.StartPhotoReplacement(station, StartProcessing(), Jpeg, 500, Limits, Now);

        Assert.IsType<StationMediaStateError>(Assert.Single(result.Errors));
        Assert.Equal(0, station.GalleryVersion);
    }

    [Fact]
    public void Publish_Replacement_TakesOverThePositionOfThePhotoItReplaces()
    {
        var station = CreateStation();
        var replaced = Publish(StartProcessing(position: 2));
        var replacement = StationMediaItem.StartPhotoReplacement(station, replaced, Jpeg, 500, Limits, Now).Value;
        replacement.CompleteUpload();

        var result = replacement.Publish(replaced, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            (2, null, StationMediaState.Published),
            (
                replacement.Position, replacement.ReplacesItemId, replacement.State));
    }

    [Fact]
    public void Publish_ReplacementWithoutThePhotoItReplaces_IsRejectedAndKeepsItsState()
    {
        var station = CreateStation();
        var replaced = Publish(StartProcessing(position: 2));
        var replacement = StationMediaItem.StartPhotoReplacement(station, replaced, Jpeg, 500, Limits, Now).Value;
        replacement.CompleteUpload();

        var result = replacement.Publish(null, Now);

        Assert.IsType<StationMediaStateError>(Assert.Single(result.Errors));
        Assert.Equal((StationMediaState.Processing, null), (replacement.State, replacement.Position));
    }

    [Fact]
    public void Reorder_EveryPhotoOnce_RewritesThePositionsAndAdvancesTheGalleryVersion()
    {
        var station = CreateStation();
        var first = StartProcessing(position: 0);
        var second = StartProcessing(position: 1);
        var third = StartProcessing(position: 4);

        var result = StationMediaItem.Reorder(
            station, [first, second, third], [third.PublicId, first.PublicId, second.PublicId]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [1, 2, 0],
            new[] { first.Position, second.Position, third.Position }.Select(value => value!.Value));
        Assert.Equal(1, station.GalleryVersion);
    }

    [Fact]
    public void Reorder_ListThatDoesNotMatchTheGallery_IsRejectedAndChangesNothing()
    {
        var station = CreateStation();
        var first = StartProcessing(position: 0);
        var second = StartProcessing(position: 1);

        foreach (var order in new[]
        {
            new[] { first.PublicId },
            [first.PublicId, first.PublicId],
            [first.PublicId, Guid.NewGuid()],
            [first.PublicId, second.PublicId, Guid.NewGuid()]
        })
        {
            Assert.IsType<StationMediaOrderError>(
                Assert.Single(StationMediaItem.Reorder(station, [first, second], order).Errors));
        }

        Assert.Equal((0, 1, 0), (first.Position, second.Position, station.GalleryVersion));
    }

    [Fact]
    public void RemoveFromGallery_PhotoWithUnfinishedReplacement_TakesBothAndAdvancesTheGalleryVersion()
    {
        var station = CreateStation();
        var published = Publish(StartProcessing());
        var replacement = StationMediaItem.StartPhotoReplacement(station, published, Jpeg, 1000, Limits, Now).Value;
        int versionBefore = station.GalleryVersion;

        var removed = published.RemoveFromGallery(station, [replacement]);

        Assert.Equal([published, replacement], removed);
        Assert.Equal(versionBefore + 1, station.GalleryVersion);
    }

    [Fact]
    public void RemoveFromGallery_PhotoWithoutReplacement_TakesOnlyItself()
    {
        var station = CreateStation();
        var uploading = StartUpload();

        var removed = uploading.RemoveFromGallery(station, []);

        Assert.Equal([uploading], removed);
        Assert.Equal(1, station.GalleryVersion);
    }

    private static StationMediaItem StartUpload(int position = 0) =>
        StationMediaItem.StartPhotoUpload(CreateStation(), 0, position, Jpeg, 1000, Limits, Now).Value;

    private static StationMediaItem StartProcessing(int position = 0)
    {
        var item = StartUpload(position);
        item.CompleteUpload();
        return item;
    }

    private static StationMediaItem Publish(StationMediaItem item)
    {
        item.Publish(null, Now);
        return item;
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
