using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Playwright;
using SkiaSharp;
using Xunit;

namespace EvCharging.E2E.Tests;

/// <summary>Drives the station media endpoints and storage the way the browser does, for the media scenarios.</summary>
internal static class StationMediaApi
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    internal static byte[] Photo(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    internal static byte[] Encode(int width, int height, SKEncodedImageFormat format)
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

    internal static (int Width, string Format) Inspect(byte[] file)
    {
        using var data = SKData.CreateCopy(file);
        using var codec = SKCodec.Create(data)!;
        return (codec.Info.Width, codec.EncodedFormat.ToString().ToLowerInvariant());
    }

    internal static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, Uri url, byte[] file, string contentType)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await client.PutAsync(url, content);
    }

    internal static Task<JsonElement> SendAsync(IPage page, string method, string path, object? body = null) =>
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

    internal static async Task<string> NorthStationIdAsync(IPage page)
    {
        var stations = await page.EvaluateAsync<JsonElement>("async () => (await fetch('/api/stations')).json()");
        return stations.GetProperty("stations").EnumerateArray()
            .First(station => station.GetProperty("name").GetString()!
                .StartsWith("North Station", StringComparison.Ordinal))
            .GetProperty("id")
            .GetString()!;
    }

    internal static async Task<JsonElement> EditorAsync(IPage page, string stationId)
    {
        var response = await SendAsync(page, "GET", $"/api/stations/{stationId}/media/edit");
        Assert.Equal(200, response.GetProperty("status").GetInt32());
        return response.GetProperty("body");
    }

    internal static async Task ClearGalleryAsync(IPage page, string stationId)
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

    internal static Task<JsonElement> RemoveAsync(IPage page, string stationId, Guid itemId) =>
        SendAsync(page, "DELETE", $"/api/stations/{stationId}/media/{itemId}");

    internal static Task<JsonElement> CompleteAsync(IPage page, string stationId, Guid itemId) =>
        SendAsync(page, "POST", $"/api/stations/{stationId}/media/{itemId}/upload-completion");

    internal static Task<JsonElement> ReorderAsync(IPage page, string stationId, int version, string[] itemIds) =>
        SendAsync(page, "PUT", $"/api/stations/{stationId}/media/order", new { galleryVersion = version, itemIds });

    internal static async Task<StartedUpload> StartUploadAsync(
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

    internal static async Task<StartedUpload> UploadAsync(
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

    internal static async Task<JsonElement> WaitForStateAsync(
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

    internal static async Task<JsonElement> WaitForReplacementAsync(
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

    internal static Task WaitForStatusAsync(HttpClient client, string url, HttpStatusCode status) =>
        WaitAsync(
            $"{url} to answer {status}",
            async () =>
            {
                var actual = (await client.GetAsync(url)).StatusCode;
                return actual == status ? null : actual.ToString();
            });

    // The condition returns null once it holds, otherwise what it saw, which the timeout reports.
    internal static async Task WaitAsync(string expectation, Func<Task<string?>> condition)
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

    internal sealed record StartedUpload(Guid ItemId, Uri UploadUrl, int ByteLength);
}
