using System.Net;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using EvCharging.S3ObjectStore;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvCharging.MediaWorker.Tests;

// The worker's handling of a video through its handlers, storage, and database. Which files the verifier accepts
// is covered by its own tests; one rejected file stands for the rest here.
[Collection(MediaWorkerCollection.Name)]
public sealed class StationVideoProcessingTests(ObjectStoreFixture storage, PlatformDatabaseFixture database)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly PhotoSeed _seed = new(database, storage);

    [Fact]
    public async Task Process_UploadedVideo_PublishesTheFileAsUploadedAndAnnouncesTheChange()
    {
        var station = await _seed.SeedStationAsync();
        var file = VideoFiles.H264Aac;
        var video = await _seed.StartVideoProcessingAsync(station, 0, file);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        var item = await WaitForStateAsync(video, StationMediaState.Published);

        Assert.NotNull(item.PublishedAt);
        var response = await storage.Browser.GetAsync(_seed.VideoUrl(station, video));
        Assert.Equal(file, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        await WaitForChangeAnnouncementsAsync(station, 1);
        await using var context = database.CreateContext("platform_migrator");
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == video.Item.Id
            && audit.Action == AuditAction.Updated && audit.ActorSubject == "media-worker"));
    }

    [Fact]
    public async Task Process_RejectedVideo_FailsWithItsReasonAndPublishesNothing()
    {
        var station = await _seed.SeedStationAsync();
        var video = await _seed.StartVideoProcessingAsync(station, 0, VideoFiles.Hevc);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        var item = await WaitForStateAsync(video, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.UnsupportedCodec, item.FailureCode);
        await AssertNotPublishedAsync(station, video);
    }

    [Fact]
    public async Task Process_FileNeverArrivesBeforeTheDeadline_FailsAsUploadMissing()
    {
        var station = await _seed.SeedStationAsync();
        var video = await _seed.StartVideoProcessingAsync(
            station, 0, null, startedAt: DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        var item = await WaitForStateAsync(video, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.UploadMissing, item.FailureCode);
        await AssertNotPublishedAsync(station, video);
    }

    // Whether the file lands before or after the notice is processed, the video ends up published.
    [Fact]
    public async Task Process_FileSentAfterTheNotice_PublishesOnceItLands()
    {
        var station = await _seed.SeedStationAsync();
        var file = VideoFiles.H264Silent;
        var video = await _seed.StartVideoProcessingAsync(station, 0, null, declaredLength: file.Length);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        (await storage.PutAsync(video.UploadUrl, file, PhotoSeed.Mp4)).EnsureSuccessStatusCode();

        await WaitForStateAsync(video, StationMediaState.Published);
    }

    [Fact]
    public async Task Process_UploadLongerThanDeclared_FailsInsteadOfStayingProcessing()
    {
        var station = await _seed.SeedStationAsync();
        var file = VideoFiles.H264Silent;
        var video = await _seed.StartVideoProcessingAsync(station, 0, null, declaredLength: file.Length - 1);
        // The video's own URL refuses a longer body, so the object is stored through a URL signed for its length.
        var longer = await storage.ApiStore.CreateUploadUrlAsync(
            StationMediaObjectKeys.Upload(station.PublicId, video.Item.PublicId),
            PhotoSeed.Mp4,
            file.Length,
            DateTimeOffset.UtcNow.AddMinutes(5));
        (await storage.PutAsync(longer.Url, file, PhotoSeed.Mp4)).EnsureSuccessStatusCode();
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        var item = await WaitForStateAsync(video, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.Undecodable, item.FailureCode);
        await AssertNotPublishedAsync(station, video);
    }

    [Fact]
    public async Task Process_DeliveredConcurrently_PublishesOnce()
    {
        var station = await _seed.SeedStationAsync();
        var video = await _seed.StartVideoProcessingAsync(station, 0, VideoFiles.H264Aac);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id)).AsTask()));
        await WaitForStateAsync(video, StationMediaState.Published);

        await WaitForChangeAnnouncementsAsync(station, 1);
        await using var context = database.CreateContext("platform_migrator");
        Assert.Equal(
            1,
            await context.AuditEntries.CountAsync(audit =>
                audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == video.Item.Id
                && audit.Action == AuditAction.Updated));
    }

    [Fact]
    public async Task Process_UploadUrlReusedAfterPublication_LeavesThePublishedBytesUnchanged()
    {
        var station = await _seed.SeedStationAsync();
        var file = VideoFiles.H264Aac;
        var video = await _seed.StartVideoProcessingAsync(station, 0, file);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);
        await host.Bus.PublishAsync(new ProcessStationMediaMessage(video.Item.Id));
        await WaitForStateAsync(video, StationMediaState.Published);

        var reuse = await storage.PutAsync(video.UploadUrl, new byte[file.Length], PhotoSeed.Mp4);

        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Equal(file, await storage.Browser.GetByteArrayAsync(_seed.VideoUrl(station, video)));
    }

    [Fact]
    public async Task Process_Replacement_TakesOverThePlaceAndCleansUpTheReplacedVideo()
    {
        var station = await _seed.SeedStationAsync();
        var replaced = await _seed.PublishedVideoAsync(station, position: 2);
        var file = VideoFiles.H264Aac;
        var replacement = await _seed.StartReplacementAsync(station, replaced, file, PhotoSeed.Mp4);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(replacement.Item.Id));
        var item = await WaitForStateAsync(replacement, StationMediaState.Published);

        Assert.Equal((StationMediaKind.Video, 2), (item.Kind, item.Position));
        Assert.Null(await _seed.FindAsync(replaced.Item.Id));
        await Polling.WaitUntilAsync(
            async () => (await storage.Browser.GetAsync(_seed.VideoUrl(station, replaced)))
                .StatusCode == HttpStatusCode.NotFound,
            Timeout);
        Assert.Equal(file, await storage.Browser.GetByteArrayAsync(_seed.VideoUrl(station, replacement)));
        await WaitForChangeAnnouncementsAsync(station, 1);
    }

    [Fact]
    public async Task Process_FailedReplacement_KeepsThePreviousVideoPublished()
    {
        var station = await _seed.SeedStationAsync();
        var replaced = await _seed.PublishedVideoAsync(station, position: 0);
        var replacement = await _seed.StartReplacementAsync(station, replaced, VideoFiles.Hevc, PhotoSeed.Mp4);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(replacement.Item.Id));
        var failed = await WaitForStateAsync(replacement, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.UnsupportedCodec, failed.FailureCode);
        var previous = await _seed.FindAsync(replaced.Item.Id);
        Assert.Equal((StationMediaState.Published, 0), (previous!.State, previous.Position));
        Assert.Equal(
            PhotoSeed.PublishedBytes, await storage.Browser.GetByteArrayAsync(_seed.VideoUrl(station, replaced)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Check_UploadStillUnfinished_ProcessesAnArrivedVideoAndFailsOtherwise(bool fileArrived)
    {
        var station = await _seed.SeedStationAsync();
        var file = VideoFiles.H264Silent;
        var video = await _seed.StartVideoUploadAsync(
            station,
            0,
            fileArrived ? file : null,
            declaredLength: file.Length,
            startedAt: DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new CheckStationMediaUploadMessage(video.Item.Id));

        var item = await WaitForStateAsync(
            video, fileArrived ? StationMediaState.Published : StationMediaState.Failed);
        Assert.Equal(fileArrived ? null : StationMediaFailure.UploadExpired, item.FailureCode);
    }

    private async Task<StationMediaItem> WaitForStateAsync(SeededPhoto video, StationMediaState state)
    {
        await Polling.WaitUntilAsync(
            async () => (await _seed.FindAsync(video.Item.Id))?.State == state, Timeout);
        return (await _seed.FindAsync(video.Item.Id))!;
    }

    private async Task AssertNotPublishedAsync(Domain.Equipment.Station station, SeededPhoto video)
    {
        var response = await storage.Browser.GetAsync(_seed.VideoUrl(station, video));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static int ChangeAnnouncements(Domain.Equipment.Station station) =>
        MediaWorkerHost.StationChangesSnapshot.Count(id => id == station.Id);

    private static Task WaitForChangeAnnouncementsAsync(Domain.Equipment.Station station, int count) =>
        Polling.WaitUntilAsync(() => Task.FromResult(ChangeAnnouncements(station) == count), Timeout);
}
