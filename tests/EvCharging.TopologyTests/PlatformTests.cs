using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.TopologyTests;

public sealed class PlatformTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    [Fact]
    [Trait("Category", "Topology")]
    public async Task UnknownBusinessRouteReturnsProblemDetailsThroughGateway()
    {
        using var client = fixture.App.CreateHttpClient("gateway", "http");
        client.Timeout = TimeSpan.FromSeconds(15);
        using var response = await client.GetAsync("/api/__unmapped-test-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task BrowserNavigatesShellAndCallsApiThroughItsOrigin()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        var frontend = fixture.App.GetEndpoint("frontend", "http");

        await page.GotoAsync(new Uri(frontend, "/missing-page").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Page not found"
        })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "Return home"
        }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Welcome to EV Charging"
        })).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Uri(frontend, "/").AbsoluteUri);

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
        Assert.Equal(200, result.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("gateway").GetString()));
        Assert.Equal("api", result.GetProperty("body").GetProperty("host").GetString());
        Assert.True(result.GetProperty("body").GetProperty("stub").GetBoolean());
    }
}
