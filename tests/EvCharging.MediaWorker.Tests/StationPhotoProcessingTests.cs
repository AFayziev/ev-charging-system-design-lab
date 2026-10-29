using System.Net;
using EvCharging.Contracts.Media;
using EvCharging.Domain.Media;
using EvCharging.Persistence.Auditing;
using EvCharging.S3ObjectStore;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SkiaSharp;
using Xunit;

namespace EvCharging.MediaWorker.Tests;

[Collection(MediaWorkerCollection.Name)]
public sealed class StationPhotoProcessingTests(ObjectStoreFixture storage, PlatformDatabaseFixture database)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly PhotoSeed _seed = new(database, storage);

    [Fact]
    public async Task Process_UploadedPhoto_PublishesBothVariantsAndAnnouncesTheChange()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, PhotoFiles.Encode(400, 300, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        var item = await WaitForStateAsync(photo, StationMediaState.Published);

        Assert.NotNull(item.PublishedAt);
        var thumbnail = await DownloadAsync(station, photo, StationMediaObjectKeys.ThumbnailVariant);
        var display = await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant);
        Assert.Equal(
            (MediaWorkerHost.ThumbnailEdge, 36, SKEncodedImageFormat.Webp),
            Facts(thumbnail));
        Assert.Equal(
            (MediaWorkerHost.DisplayEdge, 120, SKEncodedImageFormat.Webp),
            Facts(display));
        await WaitForChangeAnnouncementsAsync(station, 1);
        await using var context = database.CreateContext("platform_migrator");
        Assert.True(await context.AuditEntries.AnyAsync(audit =>
            audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == photo.Item.Id
            && audit.Action == AuditAction.Updated && audit.ActorSubject == "media-worker"));
    }

    [Fact]
    public async Task Process_PhotoWithMetadata_PublishesItWithoutTheMetadata()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(station, 0, PhotoSeed.Jpeg, PhotoFiles.JpegWithExif(80, 40));
        Assert.True((await DownloadUploadAsync(station, photo)).AsSpan().IndexOf("Exif"u8) >= 0);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await WaitForStateAsync(photo, StationMediaState.Published);

        var display = await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant);
        Assert.Equal((80, 40), Dimensions(display));
        Assert.True(display.AsSpan().IndexOf("EXIF"u8) < 0);
    }

    [Theory]
    [InlineData(StationMediaFailure.Undecodable)]
    [InlineData(StationMediaFailure.TooManyPixels)]
    [InlineData(StationMediaFailure.UnsupportedFormat)]
    public async Task Process_RejectedUpload_FailsWithItsCodeAndPublishesNothing(string failureCode)
    {
        var station = await _seed.SeedStationAsync();
        (string ContentType, byte[]? File) upload = failureCode switch
        {
            StationMediaFailure.Undecodable => (PhotoSeed.Jpeg, new byte[500]),
            StationMediaFailure.TooManyPixels => ("image/png", PhotoFiles.Encode(600, 400, SKEncodedImageFormat.Png)),
            _ => ("image/png", PhotoFiles.Gif)
        };
        var photo = await _seed.StartProcessingAsync(station, 0, upload.ContentType, upload.File);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        var item = await WaitForStateAsync(photo, StationMediaState.Failed);

        Assert.Equal(failureCode, item.FailureCode);
        await AssertNotPublishedAsync(station, photo);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Equal(0, ChangeAnnouncements(station));
    }

    [Fact]
    public async Task Process_FileArrivesAfterTheNotice_PublishesOnceItLands()
    {
        var station = await _seed.SeedStationAsync();
        var file = PhotoFiles.Encode(40, 30, SKEncodedImageFormat.Jpeg);
        var photo = await _seed.StartProcessingAsync(station, 0, PhotoSeed.Jpeg, null, declaredLength: file.Length);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(StationMediaState.Processing, (await _seed.FindAsync(photo.Item.Id))!.State);
        (await storage.PutAsync(photo.UploadUrl, file, PhotoSeed.Jpeg)).EnsureSuccessStatusCode();
        var item = await WaitForStateAsync(photo, StationMediaState.Published);

        Assert.Null(item.FailureCode);
        await WaitForChangeAnnouncementsAsync(station, 1);
    }

    [Fact]
    public async Task Process_FileNeverArrivesBeforeTheDeadline_FailsAsUploadMissing()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, null, startedAt: DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        var item = await WaitForStateAsync(photo, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.UploadMissing, item.FailureCode);
        await AssertNotPublishedAsync(station, photo);
    }

    [Fact]
    public async Task Process_UploadLongerThanDeclared_FailsInsteadOfStayingProcessing()
    {
        var station = await _seed.SeedStationAsync();
        var file = PhotoFiles.Encode(40, 30, SKEncodedImageFormat.Jpeg);
        var photo = await _seed.StartProcessingAsync(station, 0, PhotoSeed.Jpeg, null, declaredLength: file.Length - 1);
        // The photo's own URL refuses a longer body, so the object is stored through a URL signed for its length.
        var longer = await storage.ApiStore.CreateUploadUrlAsync(
            StationMediaObjectKeys.Upload(station.PublicId, photo.Item.PublicId),
            PhotoSeed.Jpeg,
            file.Length,
            DateTimeOffset.UtcNow.AddMinutes(5));
        (await storage.PutAsync(longer.Url, file, PhotoSeed.Jpeg)).EnsureSuccessStatusCode();
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        var item = await WaitForStateAsync(photo, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.Undecodable, item.FailureCode);
        await AssertNotPublishedAsync(station, photo);
    }

    [Fact]
    public async Task Process_DeliveredRepeatedlyAndConcurrently_PublishesOnce()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, PhotoFiles.Encode(100, 100, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id)).AsTask()));
        await WaitForStateAsync(photo, StationMediaState.Published);
        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(1, ChangeAnnouncements(station));
        await using var context = database.CreateContext("platform_migrator");
        Assert.Equal(
            1,
            await context.AuditEntries.CountAsync(audit =>
                audit.ResourceType == AuditResourceType.StationMediaItem && audit.ResourceId == photo.Item.Id
                && audit.Action == AuditAction.Updated));
    }

    [Fact]
    public async Task Process_CommitLostAfterVariantsWereWritten_ConvergesOnTheSameVariants()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, PhotoFiles.Encode(300, 200, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(database, storage, new LoseFirstCommit());

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await Polling.WaitUntilAsync(
            async () => (await storage.Browser.GetAsync(
                _seed.PublicUrl(station, photo, StationMediaObjectKeys.DisplayVariant))).IsSuccessStatusCode,
            Timeout);
        var written = await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant);
        await WaitForStateAsync(photo, StationMediaState.Published);

        Assert.Equal(written, await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant));
        await WaitForChangeAnnouncementsAsync(station, 1);
    }

    [Fact]
    public async Task Process_UploadUrlReusedAfterPublication_LeavesThePublishedBytesUnchanged()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, PhotoFiles.Encode(300, 200, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);
        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await WaitForStateAsync(photo, StationMediaState.Published);
        var published = await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant);

        var reuse = await storage.PutAsync(photo.UploadUrl, new byte[photo.Item.ByteLength], PhotoSeed.Jpeg);
        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Equal(published, await DownloadAsync(station, photo, StationMediaObjectKeys.DisplayVariant));
        Assert.Equal(StationMediaState.Published, (await _seed.FindAsync(photo.Item.Id))!.State);
    }

    [Fact]
    public async Task Process_Replacement_TakesOverThePlaceAndCleansUpTheReplacedPhoto()
    {
        var station = await _seed.SeedStationAsync();
        var replaced = await _seed.PublishedAsync(station, position: 3);
        var replacement = await _seed.StartReplacementAsync(
            station, replaced, PhotoFiles.Encode(200, 100, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(replacement.Item.Id));
        var item = await WaitForStateAsync(replacement, StationMediaState.Published);

        Assert.Equal(3, item.Position);
        Assert.Null(item.ReplacesItemId);
        Assert.Null(await _seed.FindAsync(replaced.Item.Id));
        await Polling.WaitUntilAsync(
            async () => (await storage.Browser.GetAsync(
                _seed.PublicUrl(station, replaced, StationMediaObjectKeys.DisplayVariant)))
                .StatusCode == HttpStatusCode.NotFound,
            Timeout);
        Assert.Equal(
            (MediaWorkerHost.DisplayEdge, 80),
            Dimensions(await DownloadAsync(station, replacement, StationMediaObjectKeys.DisplayVariant)));
        await WaitForChangeAnnouncementsAsync(station, 1);
    }

    [Fact]
    public async Task Process_FailedReplacement_KeepsThePreviousPhotoPublished()
    {
        var station = await _seed.SeedStationAsync();
        var replaced = await _seed.PublishedAsync(station, position: 0);
        var replacement = await _seed.StartReplacementAsync(station, replaced, new byte[500]);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(replacement.Item.Id));
        var failed = await WaitForStateAsync(replacement, StationMediaState.Failed);

        Assert.Equal(StationMediaFailure.Undecodable, failed.FailureCode);
        var previous = await _seed.FindAsync(replaced.Item.Id);
        Assert.Equal((StationMediaState.Published, 0), (previous!.State, previous.Position));
        Assert.Equal(
            PhotoSeed.PublishedBytes,
            await DownloadAsync(station, replaced, StationMediaObjectKeys.DisplayVariant));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Equal(0, ChangeAnnouncements(station));
    }

    [Fact]
    public async Task Process_PhotoRemovedWhileProcessing_LeavesNoFilesBehind()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartProcessingAsync(
            station, 0, PhotoSeed.Jpeg, PhotoFiles.Encode(300, 200, SKEncodedImageFormat.Jpeg));
        await using var host = await MediaWorkerHost.StartAsync(
            database, storage, new RemoveRowBeforeCommit(database, photo.Item.Id));

        await host.Bus.PublishAsync(new ProcessStationMediaMessage(photo.Item.Id));

        await Polling.WaitUntilAsync(
            async () => (await storage.Browser.GetAsync(
                _seed.PublicUrl(station, photo, StationMediaObjectKeys.DisplayVariant)))
                .StatusCode == HttpStatusCode.NotFound
                && await _seed.FindAsync(photo.Item.Id) is null,
            Timeout);
        Assert.Equal(0, ChangeAnnouncements(station));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Check_UploadStillUnfinished_ProcessesAnArrivedFileAndFailsOtherwise(bool fileArrived)
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartUploadAsync(
            station, 0, PhotoSeed.Jpeg, fileArrived ? PhotoFiles.Encode(300, 200, SKEncodedImageFormat.Jpeg) : null);
        await using var host = await MediaWorkerHost.StartAsync(database, storage);

        await host.Bus.PublishAsync(new CheckStationMediaUploadMessage(photo.Item.Id));

        var item = await WaitForStateAsync(
            photo, fileArrived ? StationMediaState.Published : StationMediaState.Failed);
        Assert.Equal(fileArrived ? null : StationMediaFailure.UploadExpired, item.FailureCode);
    }

    [Fact]
    public async Task Check_PhotoChangedWhileChecking_RetriesAndStillSettlesIt()
    {
        var station = await _seed.SeedStationAsync();
        var photo = await _seed.StartUploadAsync(station, 0, PhotoSeed.Jpeg, null);
        await using var host = await MediaWorkerHost.StartAsync(
            database, storage, new MovePhotoBeforeCommit(database, photo.Item.Id));

        await host.Bus.PublishAsync(new CheckStationMediaUploadMessage(photo.Item.Id));

        var item = await WaitForStateAsync(photo, StationMediaState.Failed);
        Assert.Equal(StationMediaFailure.UploadExpired, item.FailureCode);
    }

    private async Task<StationMediaItem> WaitForStateAsync(SeededPhoto photo, StationMediaState state)
    {
        await Polling.WaitUntilAsync(
            async () => (await _seed.FindAsync(photo.Item.Id))?.State == state, Timeout);
        return (await _seed.FindAsync(photo.Item.Id))!;
    }

    private async Task<byte[]> DownloadAsync(Domain.Equipment.Station station, SeededPhoto photo, string variant) =>
        await storage.Browser.GetByteArrayAsync(_seed.PublicUrl(station, photo, variant));

    private async Task<byte[]> DownloadUploadAsync(Domain.Equipment.Station station, SeededPhoto photo) =>
        (await storage.MediaWorkerStore.ReadPrivateAsync(
            StationMediaObjectKeys.Upload(station.PublicId, photo.Item.PublicId),
            photo.Item.ByteLength,
            CancellationToken.None))!;

    private async Task AssertNotPublishedAsync(Domain.Equipment.Station station, SeededPhoto photo)
    {
        foreach (var variant in StationMediaObjectKeys.Variants)
        {
            var response = await storage.Browser.GetAsync(_seed.PublicUrl(station, photo, variant));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static (int Width, int Height, SKEncodedImageFormat Format) Facts(byte[] file)
    {
        var facts = PhotoFiles.Inspect(file);
        return (facts.Width, facts.Height, facts.Format);
    }

    private static (int Width, int Height) Dimensions(byte[] file)
    {
        var facts = PhotoFiles.Inspect(file);
        return (facts.Width, facts.Height);
    }

    private static int ChangeAnnouncements(Domain.Equipment.Station station) =>
        MediaWorkerHost.StationChangesSnapshot.Count(id => id == station.Id);

    private static Task WaitForChangeAnnouncementsAsync(Domain.Equipment.Station station, int count) =>
        Polling.WaitUntilAsync(() => Task.FromResult(ChangeAnnouncements(station) == count), Timeout);

    // The first commit is lost after storage has taken the variants, as when the connection drops.
    private sealed class LoseFirstCommit : SaveChangesInterceptor
    {
        private int _lost;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _lost, 1) == 0
                ? throw new DbUpdateException("lost", new NpgsqlException("connection lost", new IOException()))
                : ValueTask.FromResult(result);
    }

    // A reorder commits while the worker is still holding the photo it loaded.
    private sealed class MovePhotoBeforeCommit(PlatformDatabaseFixture database, Guid itemId) : SaveChangesInterceptor
    {
        private int _moved;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _moved, 1) == 0)
            {
                await using var connection = database.CreateConnection("platform_migrator");
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    "UPDATE charging.station_media_items SET position = position + 1 WHERE id = @id", connection);
                command.Parameters.AddWithValue("id", itemId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    // A gallery removal commits while the worker is still holding the photo it loaded.
    private sealed class RemoveRowBeforeCommit(PlatformDatabaseFixture database, Guid itemId) : SaveChangesInterceptor
    {
        private int _removed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _removed, 1) == 0)
            {
                await using var connection = database.CreateConnection("platform_migrator");
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    "DELETE FROM charging.station_media_items WHERE id = @id", connection);
                command.Parameters.AddWithValue("id", itemId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
