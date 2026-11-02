using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using EvCharging.S3ObjectStore;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.MediaWorker.Tests;

internal sealed class PhotoSeed(PlatformDatabaseFixture database, ObjectStoreFixture storage)
{
    public const string Jpeg = "image/jpeg";
    public const string Mp4 = VideoFiles.Mp4;
    private static readonly StationMediaLimits Limits = new(
        10,
        1_000_000,
        new HashSet<string>(StringComparer.Ordinal) { Jpeg, "image/png", "image/webp" },
        1,
        1_000_000,
        new HashSet<string>(StringComparer.Ordinal) { Mp4 },
        TimeSpan.FromMinutes(30));

    public async Task<Station> SeedStationAsync()
    {
        await using var context = database.CreateContext("platform_migrator");
        var station = new Station
        {
            Name = "Media test",
            Address = "1 Synthetic Avenue",
            TimeZoneId = "Europe/London",
            AccessInstructions = "Test"
        };
        context.Stations.Add(station);
        await context.SaveChangesAsync();
        return station;
    }

    // A photo whose upload has started. The file reaches storage through the signed URL, as a browser would send
    // it, unless the test leaves it out.
    public async Task<SeededPhoto> StartUploadAsync(
        Station station,
        int position,
        string contentType,
        byte[]? file,
        long? declaredLength = null,
        DateTimeOffset? startedAt = null)
    {
        await using var context = database.CreateContext("platform_migrator");
        var tracked = await context.Stations.SingleAsync(value => value.Id == station.Id);
        var byteLength = declaredLength ?? file?.Length ?? 1;
        var item = StationMediaItem.StartPhotoUpload(
            tracked, 0, position, contentType, byteLength, Limits, startedAt ?? DateTimeOffset.UtcNow).Value;
        context.StationMediaItems.Add(item);
        await context.SaveChangesAsync();
        return await UploadAsync(station, item, file);
    }

    public async Task<SeededPhoto> StartProcessingAsync(
        Station station,
        int position,
        string contentType,
        byte[]? file,
        long? declaredLength = null,
        DateTimeOffset? startedAt = null)
    {
        var photo = await StartUploadAsync(station, position, contentType, file, declaredLength, startedAt);
        await ChangeAsync(photo.Item.Id, item => item.CompleteUpload());
        return photo;
    }

    // A published photo whose variants are in the public bucket, standing in for earlier processing.
    public async Task<SeededPhoto> PublishedAsync(Station station, int position)
    {
        var photo = await StartProcessingAsync(
            station,
            position,
            Jpeg,
            PhotoFiles.Encode(10, 10, SkiaSharp.SKEncodedImageFormat.Jpeg));
        await ChangeAsync(photo.Item.Id, item => item.Publish(null, DateTimeOffset.UtcNow));
        foreach (var variant in StationMediaObjectKeys.Variants)
        {
            using var content = new MemoryStream(PublishedBytes);
            await storage.MediaWorkerStore.PutPublicAsync(
                StationMediaObjectKeys.Variant(station.PublicId, photo.Item.PublicId, variant),
                content,
                "image/webp",
                CancellationToken.None);
        }

        return photo;
    }

    public async Task<SeededPhoto> StartReplacementAsync(
        Station station, SeededPhoto replaced, byte[] file, string contentType = Jpeg)
    {
        await using var context = database.CreateContext("platform_migrator");
        var tracked = await context.Stations.SingleAsync(value => value.Id == station.Id);
        var target = await context.StationMediaItems.SingleAsync(value => value.Id == replaced.Item.Id);
        var item = StationMediaItem.StartReplacement(
            tracked, target, contentType, file.Length, Limits, DateTimeOffset.UtcNow).Value;
        context.StationMediaItems.Add(item);
        await context.SaveChangesAsync();
        var photo = await UploadAsync(station, item, file);
        await ChangeAsync(item.Id, value => value.CompleteUpload());
        return photo;
    }

    // A video whose upload has started. The file reaches storage through the signed URL unless the test leaves it out.
    public async Task<SeededPhoto> StartVideoUploadAsync(
        Station station,
        int position,
        byte[]? file,
        long? declaredLength = null,
        DateTimeOffset? startedAt = null)
    {
        await using var context = database.CreateContext("platform_migrator");
        var tracked = await context.Stations.SingleAsync(value => value.Id == station.Id);
        var byteLength = declaredLength ?? file?.Length ?? 1;
        var item = StationMediaItem.StartVideoUpload(
            tracked, 0, position, Mp4, byteLength, Limits, startedAt ?? DateTimeOffset.UtcNow).Value;
        context.StationMediaItems.Add(item);
        await context.SaveChangesAsync();
        return await UploadAsync(station, item, file);
    }

    // A video whose upload has been reported, as a browser would do it.
    public async Task<SeededPhoto> StartVideoProcessingAsync(
        Station station,
        int position,
        byte[]? file,
        long? declaredLength = null,
        DateTimeOffset? startedAt = null)
    {
        var video = await StartVideoUploadAsync(station, position, file, declaredLength, startedAt);
        await ChangeAsync(video.Item.Id, item => item.CompleteUpload());
        return video;
    }

    // A published video whose file is in the public bucket, standing in for earlier processing.
    public async Task<SeededPhoto> PublishedVideoAsync(Station station, int position)
    {
        var video = await StartVideoProcessingAsync(station, position, VideoFiles.H264Silent);
        await ChangeAsync(video.Item.Id, item => item.Publish(null, DateTimeOffset.UtcNow));
        using var content = new MemoryStream(PublishedBytes);
        await storage.MediaWorkerStore.PutPublicAsync(
            StationMediaObjectKeys.Video(station.PublicId, video.Item.PublicId),
            content,
            Mp4,
            CancellationToken.None);
        return video;
    }

    public Uri VideoUrl(Station station, SeededPhoto video) =>
        storage.PublicUrl(StationMediaObjectKeys.Video(station.PublicId, video.Item.PublicId));

    public static byte[] PublishedBytes { get; } = "earlier published bytes"u8.ToArray();

    public Uri PublicUrl(Station station, SeededPhoto photo, string variant) =>
        storage.PublicUrl(StationMediaObjectKeys.Variant(station.PublicId, photo.Item.PublicId, variant));

    public async Task<StationMediaItem?> FindAsync(Guid id)
    {
        await using var context = database.CreateContext("platform_migrator");
        return await context.StationMediaItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
    }

    private async Task ChangeAsync(Guid id, Action<StationMediaItem> change)
    {
        await using var context = database.CreateContext("platform_migrator");
        var item = await context.StationMediaItems.SingleAsync(value => value.Id == id);
        change(item);
        await context.SaveChangesAsync();
    }

    private async Task<SeededPhoto> UploadAsync(Station station, StationMediaItem item, byte[]? file)
    {
        var upload = await storage.ApiStore.CreateUploadUrlAsync(
            StationMediaObjectKeys.Upload(station.PublicId, item.PublicId),
            item.ContentType,
            item.ByteLength,
            DateTimeOffset.UtcNow.AddMinutes(30));
        if (file is not null)
        {
            (await storage.PutAsync(upload.Url, file, item.ContentType)).EnsureSuccessStatusCode();
        }

        return new SeededPhoto(item, upload.Url);
    }
}

internal sealed record SeededPhoto(StationMediaItem Item, Uri UploadUrl);
