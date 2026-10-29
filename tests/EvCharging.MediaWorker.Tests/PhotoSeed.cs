using EvCharging.Domain.Equipment;
using EvCharging.Domain.Media;
using EvCharging.S3ObjectStore;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.MediaWorker.Tests;

internal sealed class PhotoSeed(PlatformDatabaseFixture database, ObjectStoreFixture storage)
{
    public const string Jpeg = "image/jpeg";
    private static readonly StationMediaLimits Limits = new(
        10,
        1_000_000,
        new HashSet<string>(StringComparer.Ordinal) { Jpeg, "image/png", "image/webp" },
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
        Station station, SeededPhoto replaced, byte[] file)
    {
        await using var context = database.CreateContext("platform_migrator");
        var tracked = await context.Stations.SingleAsync(value => value.Id == station.Id);
        var target = await context.StationMediaItems.SingleAsync(value => value.Id == replaced.Item.Id);
        var item = StationMediaItem.StartPhotoReplacement(
            tracked, target, Jpeg, file.Length, Limits, DateTimeOffset.UtcNow).Value;
        context.StationMediaItems.Add(item);
        await context.SaveChangesAsync();
        var photo = await UploadAsync(station, item, file);
        await ChangeAsync(item.Id, value => value.CompleteUpload());
        return photo;
    }

    public static byte[] PublishedBytes { get; } = "earlier published bytes"u8.ToArray();

    public Uri PublicUrl(Station station, SeededPhoto photo, string variant) =>
        storage.MediaWorkerStore
            .PublicUrl(StationMediaObjectKeys.Variant(station.PublicId, photo.Item.PublicId, variant));

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
