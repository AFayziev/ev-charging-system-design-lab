using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class PlatformTests(AppHostFixture fixture)
{
    [Fact]
    [Trait("Category", "Api")]
    public async Task Route_AnonymousBrowser_ReturnsUnauthorized()
    {
        using var client = fixture.App.CreateHttpClient("gateway", "http");
        client.Timeout = TimeSpan.FromSeconds(15);

        using var response = await client.GetAsync("/api/__unmapped-test-route");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Navigate_SameOrigin_RoutesApi()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        var sentryRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath == "/diagnostics/sentry"
                || new Uri(request.Url).Host.EndsWith(".sentry.io", StringComparison.OrdinalIgnoreCase))
            {
                sentryRequests.Add(new Uri(request.Url).GetLeftPart(UriPartial.Path));
            }
        };
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

        var result = await page.EvaluateAsync<JsonElement>("""
            async () => {
                const response = await fetch('/api/diagnostics/instance', {
                    signal: AbortSignal.timeout(15000)
                });
                return {
                    status: response.status,
                    gateway: response.headers.get('X-Gateway-Instance'),
                    body: await response.json()
                };
            }
            """);
        Assert.Empty(sentryRequests);
        using var gateway = fixture.App.CreateHttpClient("gateway", "http");
        using var disabledTunnel = await gateway.PostAsync("/diagnostics/sentry", new StringContent("synthetic"));
        Assert.Equal(HttpStatusCode.NotFound, disabledTunnel.StatusCode);
        Assert.Equal(401, result.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("gateway").GetString()));
    }
}
