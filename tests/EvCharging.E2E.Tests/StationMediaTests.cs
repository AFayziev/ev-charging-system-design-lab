using System.Net;
using Microsoft.Playwright;
using SkiaSharp;
using Xunit;
using static EvCharging.E2E.Tests.StationMediaApi;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class StationMediaTests(AppHostFixture fixture)
{
    [Fact]
    [Trait("Category", "Api")]
    public async Task Gallery_UploadReplaceReorderAndRemove_WorkThroughTheGateway()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "operator");
        using var storage = new HttpClient();
        string stationId = await NorthStationIdAsync(page);
        await ClearGalleryAsync(page, stationId);

        // A photo uploaded and completed is processed and published with both variants.
        var first = await UploadAsync(page, storage, stationId, Photo(1000, 750), SKEncodedImageFormat.Jpeg);
        var published = await WaitForStateAsync(page, stationId, first.ItemId, "published");
        var thumbnail = await storage.GetByteArrayAsync(published.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(480, Inspect(thumbnail).Width);
        Assert.Equal("webp", Inspect(thumbnail).Format);

        // A file that cannot be decoded fails with its reason, and its place is free again.
        var broken = await StartUploadAsync(page, stationId, new byte[600], "image/jpeg");
        (await PutAsync(storage, broken.UploadUrl, new byte[600], "image/jpeg")).EnsureSuccessStatusCode();
        Assert.Equal(200, (await CompleteAsync(page, stationId, broken.ItemId)).GetProperty("status").GetInt32());
        var failed = await WaitForStateAsync(page, stationId, broken.ItemId, "failed");
        Assert.Equal("undecodable", failed.GetProperty("failureCode").GetString());
        Assert.Equal(409, (await CompleteAsync(page, stationId, broken.ItemId)).GetProperty("status").GetInt32());

        // A replacement that fails leaves the published photo in place; a good one takes over its position.
        var badReplacement = await StartUploadAsync(page, stationId, new byte[600], "image/jpeg", first.ItemId);
        (await PutAsync(storage, badReplacement.UploadUrl, new byte[600], "image/jpeg")).EnsureSuccessStatusCode();
        await CompleteAsync(page, stationId, badReplacement.ItemId);
        var withFailedReplacement = await WaitForReplacementAsync(page, stationId, first.ItemId, "failed");
        Assert.Equal("published", withFailedReplacement.GetProperty("state").GetString());
        Assert.Equal(thumbnail, await storage.GetByteArrayAsync(published.GetProperty("thumbnailUrl").GetString()));
        var goodReplacement = await UploadAsync(
            page, storage, stationId, Photo(200, 100), SKEncodedImageFormat.Jpeg, first.ItemId);
        await WaitForStateAsync(page, stationId, goodReplacement.ItemId, "published");
        var gallery = await EditorAsync(page, stationId);
        Assert.DoesNotContain(
            gallery.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == first.ItemId.ToString());
        await WaitForStatusAsync(storage, published.GetProperty("thumbnailUrl").GetString()!, HttpStatusCode.NotFound);

        // Photos reorder against the gallery version they were read at, and a stale version is refused.
        var second = await UploadAsync(
            page, storage, stationId, Encode(300, 300, SKEncodedImageFormat.Png), SKEncodedImageFormat.Png);
        await WaitForStateAsync(page, stationId, second.ItemId, "published");
        gallery = await EditorAsync(page, stationId);
        int version = gallery.GetProperty("galleryVersion").GetInt32();
        var order = gallery.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .Reverse()
            .ToArray();
        Assert.Equal(200, (await ReorderAsync(page, stationId, version, order)).GetProperty("status").GetInt32());
        Assert.Equal(409, (await ReorderAsync(page, stationId, version, order)).GetProperty("status").GetInt32());
        Assert.Equal(
            order,
            (await EditorAsync(page, stationId)).GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()!));

        // Removal hides the photo at once and deletes its files.
        var secondPhoto = await EditorAsync(page, stationId);
        string removedThumbnail = secondPhoto.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == second.ItemId.ToString())
            .GetProperty("thumbnailUrl")
            .GetString()!;
        Assert.Equal(200, (await RemoveAsync(page, stationId, second.ItemId)).GetProperty("status").GetInt32());
        Assert.DoesNotContain(
            (await EditorAsync(page, stationId)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == second.ItemId.ToString());
        await WaitForStatusAsync(storage, removedThumbnail, HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Gallery_UploadNeverCompleted_FailsAtItsDeadlineOrProcessesAnArrivedFile()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "operator");
        using var storage = new HttpClient();
        string stationId = await NorthStationIdAsync(page);
        await ClearGalleryAsync(page, stationId);

        // The browser sends the file but its completion notice is lost; the other upload never arrives at all.
        var arrived = await StartUploadAsync(page, stationId, Photo(300, 200), "image/jpeg");
        (await PutAsync(storage, arrived.UploadUrl, Photo(300, 200), "image/jpeg")).EnsureSuccessStatusCode();
        var missing = await StartUploadAsync(page, stationId, Photo(300, 200), "image/jpeg");

        await WaitForStateAsync(page, stationId, arrived.ItemId, "published");
        var expired = await WaitForStateAsync(page, stationId, missing.ItemId, "failed");
        Assert.Equal("upload_expired", expired.GetProperty("failureCode").GetString());
    }
}
