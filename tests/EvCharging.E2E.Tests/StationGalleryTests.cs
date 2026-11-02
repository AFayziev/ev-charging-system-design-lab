using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using SkiaSharp;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
[Trait("Category", "Browser")]
public sealed class StationGalleryTests(AppHostFixture fixture)
{
    private const float Patience = 60_000;
    private static readonly string Screenshots =
        Path.Combine(AppContext.BaseDirectory, "gallery-screenshots");

    [Fact]
    public async Task Gallery_OperatorUploadsReordersAndReplaces_DriverSeesTheCoverAndTheGallery()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var operatorPage = await browser.NewPageAsync();
        await fixture.LoginAsync(operatorPage, "operator");
        string stationId = await StationMediaApi.NorthStationIdAsync(operatorPage);
        await StationMediaApi.ClearGalleryAsync(operatorPage, stationId);

        // Two photos are sent together and both end up published.
        await operatorPage.GotoAsync(Url($"/stations/{stationId}/edit"));
        await Expect(operatorPage.GetByText("0 / 5 photos")).ToBeVisibleAsync();
        await FileInput(operatorPage).SetInputFilesAsync(
            [Payload("entrance.jpg", 800, 600), Payload("bays.jpg", 640, 480)]);
        await Expect(operatorPage.GetByRole(AriaRole.Button, new() { Name = "Replace photo 2" }))
            .ToBeVisibleAsync(new() { Timeout = Patience });
        await Expect(operatorPage.GetByRole(AriaRole.Button, new() { Name = "Replace photo 1" }))
            .ToBeVisibleAsync(new() { Timeout = Patience });
        var uploaded = await ItemIdsAsync(operatorPage, stationId);
        Assert.Equal(2, uploaded.Count);

        // Moving the second photo earlier makes it the cover.
        await operatorPage.GetByRole(AriaRole.Button, new() { Name = "Move photo 2 earlier" }).ClickAsync();
        await WaitForCoverAsync(operatorPage, stationId, uploaded[1]);

        // Replacing the cover keeps its place once the new photo is published.
        var chooser = await operatorPage.RunAndWaitForFileChooserAsync(
            () => operatorPage.GetByRole(AriaRole.Button, new() { Name = "Replace photo 1" }).ClickAsync());
        await chooser.SetFilesAsync(Payload("replacement.jpg", 700, 700));
        string coverId = await WaitForNewCoverAsync(operatorPage, stationId, uploaded[1]);

        await using var driverContext = await browser.NewContextAsync();
        var driver = await driverContext.NewPageAsync();
        await fixture.LoginAsync(driver, "driver");
        await WaitForSearchCoverAsync(driver, stationId, coverId);
        Directory.CreateDirectory(Screenshots);
        foreach (var (name, width, height) in new[] { ("desktop", 1280, 900), ("mobile", 390, 844) })
        {
            await driver.SetViewportSizeAsync(width, height);

            // The catalog card shows the cover.
            await driver.GotoAsync(Url("/stations"));
            await Expect(Card(driver).Locator("img"))
                .ToHaveAttributeAsync("src", new Regex(coverId), new() { Timeout = Patience });
            await AssertNoHorizontalScrollAsync(driver);
            await driver.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"catalog-{name}.png") });

            // Station detail shows both photos, the cover first.
            await driver.GotoAsync(Url($"/stations/{stationId}"));
            var gallery = driver.GetByRole(AriaRole.Region, new() { Name = "Station photos" });
            await Expect(gallery.Locator("figure img"))
                .ToHaveAttributeAsync("src", new Regex(coverId), new() { Timeout = Patience });
            await Expect(gallery.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Show photo") }))
                .ToHaveCountAsync(2);
            await AssertNoHorizontalScrollAsync(driver);
            await driver.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"detail-{name}.png") });
        }
    }

    [Fact]
    public async Task Gallery_UndecodableUpload_ShowsItsReasonAndCanBeRemoved()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "operator");
        string stationId = await StationMediaApi.NorthStationIdAsync(page);
        await StationMediaApi.ClearGalleryAsync(page, stationId);

        await page.GotoAsync(Url($"/stations/{stationId}/edit"));
        await Expect(page.GetByText("0 / 5 photos")).ToBeVisibleAsync();
        await FileInput(page).SetInputFilesAsync(
            new FilePayload { Name = "broken.jpg", MimeType = "image/jpeg", Buffer = new byte[600] });
        await Expect(page.GetByText("This file isn't a readable image."))
            .ToBeVisibleAsync(new() { Timeout = Patience });

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove photo 1" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm removal of photo 1" }).ClickAsync();
        await Expect(page.GetByText("No photos yet.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Gallery_StorageUnreachable_BrowsingStaysUsableAndUploadsReportIt()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var operatorPage = await browser.NewPageAsync();
        await fixture.LoginAsync(operatorPage, "operator");
        string stationId = await StationMediaApi.NorthStationIdAsync(operatorPage);
        await StationMediaApi.ClearGalleryAsync(operatorPage, stationId);
        using var storage = new HttpClient();
        var photo = await StationMediaApi.UploadAsync(
            operatorPage, storage, stationId, StationMediaApi.Photo(800, 600), SKEncodedImageFormat.Jpeg);
        await StationMediaApi.WaitForStateAsync(operatorPage, stationId, photo.ItemId, "published");

        // Published photos cannot be fetched: cards and detail show placeholders and the station stays usable.
        await using var driverContext = await browser.NewContextAsync();
        await driverContext.RouteAsync("**/public-media/**", route => route.AbortAsync());
        var driver = await driverContext.NewPageAsync();
        await fixture.LoginAsync(driver, "driver");
        await driver.GotoAsync(Url("/stations"));
        await Expect(Card(driver).Locator("div[role=img]")).ToBeVisibleAsync(new() { Timeout = Patience });
        await driver.GotoAsync(Url($"/stations/{stationId}"));
        await Expect(driver.GetByRole(AriaRole.Region, new() { Name = "Station photos" })
            .GetByRole(AriaRole.Img, new() { Name = "Photo unavailable" }))
            .ToBeVisibleAsync();
        await Expect(driver.GetByRole(AriaRole.Heading, new() { Name = "Charging equipment" })).ToBeVisibleAsync();

        // Uploads cannot reach storage: the operator is told, and the published photo is untouched.
        await operatorPage.RouteAsync("**/private-files/**", route => route.AbortAsync());
        await operatorPage.GotoAsync(Url($"/stations/{stationId}/edit"));
        await Expect(operatorPage.GetByText("1 / 5 photos")).ToBeVisibleAsync(new() { Timeout = Patience });
        await FileInput(operatorPage).SetInputFilesAsync(Payload("offline.jpg", 400, 300));
        await Expect(operatorPage.GetByText("Photo storage is unavailable"))
            .ToBeVisibleAsync(new() { Timeout = Patience });
        await Expect(operatorPage.Locator("article[aria-label^='Photo']")).ToHaveCountAsync(1);
    }

    private string Url(string path) => new Uri(fixture.App.GetEndpoint("frontend", "http"), path).AbsoluteUri;

    private static ILocator FileInput(IPage page) => page.Locator("input[type=file]:not([data-testid])");

    private static ILocator Card(IPage page) =>
        page.Locator("article").Filter(new() { HasText = "North Station" }).First;

    private static FilePayload Payload(string name, int width, int height) =>
        new() { Name = name, MimeType = "image/jpeg", Buffer = StationMediaApi.Photo(width, height) };

    private static async Task<List<string>> ItemIdsAsync(IPage page, string stationId) =>
        (await StationMediaApi.EditorAsync(page, stationId)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToList();

    private static Task WaitForCoverAsync(IPage page, string stationId, string expected) =>
        StationMediaApi.WaitAsync(
            "the moved photo to become the cover",
            async () =>
            {
                var ids = await ItemIdsAsync(page, stationId);
                return ids.FirstOrDefault() == expected ? null : string.Join(",", ids);
            });

    // The replacement is published under its own id and takes the replaced photo's place.
    private static async Task<string> WaitForNewCoverAsync(IPage page, string stationId, string replaced)
    {
        string? cover = null;
        await StationMediaApi.WaitAsync(
            "the replacement to take the cover",
            async () =>
            {
                var items = (await StationMediaApi.EditorAsync(page, stationId)).GetProperty("items");
                var first = items.EnumerateArray().First();
                cover = first.GetProperty("id").GetString();
                bool published = first.GetProperty("state").GetString() == "published";
                return cover != replaced && published ? null : $"{cover} {first.GetProperty("state")}";
            });
        return cover!;
    }

    private static Task WaitForSearchCoverAsync(IPage page, string stationId, string coverId) =>
        StationMediaApi.WaitAsync(
            "search to carry the cover",
            async () =>
            {
                var response = await StationMediaApi.SendAsync(page, "GET", "/api/search/stations?q=North");
                var found = response.GetProperty("body").GetProperty("stations").EnumerateArray()
                    .FirstOrDefault(station => station.GetProperty("id").GetString() == stationId);
                string? cover = found.ValueKind == JsonValueKind.Object
                    && found.GetProperty("coverPhoto").ValueKind == JsonValueKind.Object
                        ? found.GetProperty("coverPhoto").GetProperty("id").GetString()
                        : null;
                return cover == coverId ? null : cover ?? "no cover";
            });

    private static async Task AssertNoHorizontalScrollAsync(IPage page) =>
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
}
