using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class PlatformTests(AppHostFixture fixture)
{
    [Fact]
    [Trait("Category", "Browser")]
    public async Task Navigate_UnknownRoute_ReturnsToCatalogue()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        var frontend = fixture.App.GetEndpoint("frontend", "http");

        await page.GotoAsync(new Uri(frontend, "/missing-page").AbsoluteUri);
        await Expect(page.GetByRole(
            AriaRole.Heading,
            new()
            {
                Name = "Page not found"
            }))
            .ToBeVisibleAsync();
        await page.GetByRole(
            AriaRole.Link,
            new()
            {
                Name = "Return home"
            })
            .ClickAsync();
        await Expect(page.GetByRole(
            AriaRole.Heading,
            new()
            {
                Name = "Find a station"
            }))
            .ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Uri(frontend, "/stations").AbsoluteUri);
    }
}
