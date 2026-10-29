using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Playwright;
using SkiaSharp;
using Xunit;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class StationMediaTests(AppHostFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

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

    private static byte[] Photo(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static (int Width, string Format) Inspect(byte[] file)
    {
        using var data = SKData.CreateCopy(file);
        using var codec = SKCodec.Create(data)!;
        return (codec.Info.Width, codec.EncodedFormat.ToString().ToLowerInvariant());
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, Uri url, byte[] file, string contentType)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await client.PutAsync(url, content);
    }

    private static Task<JsonElement> SendAsync(IPage page, string method, string path, object? body = null) =>
        page.EvaluateAsync<JsonElement>(
            """
            async ({ method, path, body }) => {
                const session = await (await fetch('/auth/session')).json();
                const headers = { 'X-CSRF-TOKEN': session.antiforgeryToken };
                if (body) { headers['Content-Type'] = 'application/json'; }
                const response = await fetch(path, {
                    method, headers, body: body ? JSON.stringify(body) : undefined
                });
                const text = await response.text();
                return { status: response.status, body: text ? JSON.parse(text) : null };
            }
            """,
            new { method, path, body });

    private static async Task<string> NorthStationIdAsync(IPage page)
    {
        var stations = await page.EvaluateAsync<JsonElement>("async () => (await fetch('/api/stations')).json()");
        return stations.GetProperty("stations").EnumerateArray()
            .First(station => station.GetProperty("name").GetString()!
                .StartsWith("North Station", StringComparison.Ordinal))
            .GetProperty("id")
            .GetString()!;
    }

    private static async Task<JsonElement> EditorAsync(IPage page, string stationId)
    {
        var response = await SendAsync(page, "GET", $"/api/stations/{stationId}/media/edit");
        Assert.Equal(200, response.GetProperty("status").GetInt32());
        return response.GetProperty("body");
    }

    private static async Task ClearGalleryAsync(IPage page, string stationId)
    {
        foreach (var item in (await EditorAsync(page, stationId)).GetProperty("items").EnumerateArray())
        {
            Assert.Equal(
                200,
                (await RemoveAsync(page, stationId, Guid.Parse(item.GetProperty("id").GetString()!)))
                    .GetProperty("status")
                    .GetInt32());
        }
    }

    private static Task<JsonElement> RemoveAsync(IPage page, string stationId, Guid itemId) =>
        SendAsync(page, "DELETE", $"/api/stations/{stationId}/media/{itemId}");

    private static Task<JsonElement> CompleteAsync(IPage page, string stationId, Guid itemId) =>
        SendAsync(page, "POST", $"/api/stations/{stationId}/media/{itemId}/upload-completion");

    private static Task<JsonElement> ReorderAsync(IPage page, string stationId, int version, string[] itemIds) =>
        SendAsync(page, "PUT", $"/api/stations/{stationId}/media/order", new { galleryVersion = version, itemIds });

    private static async Task<StartedUpload> StartUploadAsync(
        IPage page, string stationId, byte[] file, string contentType, Guid? replacesItemId = null)
    {
        int version = (await EditorAsync(page, stationId)).GetProperty("galleryVersion").GetInt32();
        var response = await SendAsync(
            page,
            "POST",
            $"/api/stations/{stationId}/media/photo-uploads",
            new { galleryVersion = version, contentType, byteLength = file.Length, replacesItemId });
        Assert.Equal(201, response.GetProperty("status").GetInt32());
        var body = response.GetProperty("body");
        return new StartedUpload(
            Guid.Parse(body.GetProperty("itemId").GetString()!),
            new Uri(body.GetProperty("uploadUrl").GetString()!),
            file.Length);
    }

    private static async Task<StartedUpload> UploadAsync(
        IPage page,
        HttpClient storage,
        string stationId,
        byte[] file,
        SKEncodedImageFormat format,
        Guid? replacesItemId = null)
    {
        string contentType = format == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg";
        var started = await StartUploadAsync(page, stationId, file, contentType, replacesItemId);
        (await PutAsync(storage, started.UploadUrl, file, contentType)).EnsureSuccessStatusCode();
        Assert.Equal(200, (await CompleteAsync(page, stationId, started.ItemId)).GetProperty("status").GetInt32());
        return started;
    }

    private static async Task<JsonElement> WaitForStateAsync(
        IPage page, string stationId, Guid itemId, string state)
    {
        JsonElement found = default;
        await WaitAsync(
            $"photo {itemId} to be {state}",
            async () =>
            {
                var items = (await EditorAsync(page, stationId)).GetProperty("items").EnumerateArray().ToArray();
                foreach (var item in items)
                {
                    if (item.GetProperty("id").GetString() == itemId.ToString()
                        && item.GetProperty("state").GetString() == state)
                    {
                        found = item.Clone();
                        return null;
                    }
                }

                return string.Join("; ", items.Select(item => item.ToString()));
            });
        return found;
    }

    private static async Task<JsonElement> WaitForReplacementAsync(
        IPage page, string stationId, Guid itemId, string state)
    {
        JsonElement found = default;
        await WaitAsync(
            $"the replacement of photo {itemId} to be {state}",
            async () =>
            {
                var items = (await EditorAsync(page, stationId)).GetProperty("items").EnumerateArray().ToArray();
                foreach (var item in items)
                {
                    if (item.GetProperty("id").GetString() == itemId.ToString()
                        && item.TryGetProperty("replacement", out var replacement)
                        && replacement.ValueKind == JsonValueKind.Object
                        && replacement.GetProperty("state").GetString() == state)
                    {
                        found = item.Clone();
                        return null;
                    }
                }

                return string.Join("; ", items.Select(item => item.ToString()));
            });
        return found;
    }

    private static Task WaitForStatusAsync(HttpClient client, string url, HttpStatusCode status) =>
        WaitAsync(
            $"{url} to answer {status}",
            async () =>
            {
                var actual = (await client.GetAsync(url)).StatusCode;
                return actual == status ? null : actual.ToString();
            });

    // The condition returns null once it holds, otherwise what it saw, which the timeout reports.
    private static async Task WaitAsync(string expectation, Func<Task<string?>> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        string? seen;
        while ((seen = await condition()) is not null)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Timed out waiting for {expectation}. Last seen: {seen}");
            }
        }
    }

    private sealed record StartedUpload(Guid ItemId, Uri UploadUrl, int ByteLength);
}
