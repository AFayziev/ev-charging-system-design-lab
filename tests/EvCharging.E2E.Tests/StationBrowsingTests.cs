using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class StationBrowsingTests(AppHostFixture fixture)
{
    [Theory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    [Trait("Category", "Browser")]
    public async Task Browse_Anonymous_SeededEquipmentVisible(int width, int height)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(
            new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/").AbsoluteUri);
        await Expect(page).ToHaveURLAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/stations")
            .AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a station" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "North Station", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "South Station", Exact = true }))
            .ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        await page.WaitForFunctionAsync(
            "Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)");
        string screenshots = Path.Combine(AppContext.BaseDirectory, "station-screenshots");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"catalog-{width}.png"), FullPage = true });

        var first = await page.EvaluateAsync<JsonElement>(
            "async () => (await fetch('/api/stations?limit=1')).json()");
        var station = first.GetProperty("stations")[0];
        Assert.Equal("North Station", station.GetProperty("name").GetString());
        Assert.Equal(2, station.GetProperty("connectorCount").GetInt32());
        Assert.Equal(1, station.GetProperty("chargerCount").GetInt32());
        Assert.Equal(2, station.GetProperty("evseCount").GetInt32());
        var stationId = station.GetProperty("id").GetGuid();
        string cursor = first.GetProperty("nextAfterId").GetString()!;
        Assert.Equal(stationId.ToString(), cursor);
        var second = await page.EvaluateAsync<JsonElement>(
            "async cursor => (await fetch('/api/stations?limit=1&afterId=' + cursor)).json()", cursor);
        Assert.Equal("South Station", second.GetProperty("stations")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, second.GetProperty("nextAfterId").ValueKind);
        var remaining = await page.EvaluateAsync<JsonElement>(
            "async cursor => (await fetch('/api/stations?afterId=' + cursor)).json()",
            second.GetProperty("nextAfterId").GetString());
        var remainingStations = remaining.GetProperty("stations").EnumerateArray().ToArray();
        Assert.NotEmpty(remainingStations);
        Assert.Equal(JsonValueKind.Null, remaining.GetProperty("nextAfterId").ValueKind);
        var empty = await page.EvaluateAsync<JsonElement>(
            "async cursor => (await fetch('/api/stations?afterId=' + cursor)).json()",
            remainingStations[^1].GetProperty("id").GetString());
        Assert.Empty(empty.GetProperty("stations").EnumerateArray());
        var invalidPage = await page.EvaluateAsync<JsonElement>(
            """
            async cursor => {
                const response = await fetch('/api/stations?afterId=' + cursor + '&limit=51');
                return {
                    status: response.status,
                    contentType: response.headers.get('content-type'),
                    body: await response.json()
                };
            }
            """,
            Guid.NewGuid().ToString());
        Assert.Equal(400, invalidPage.GetProperty("status").GetInt32());
        Assert.Equal("application/problem+json", invalidPage.GetProperty("contentType").GetString());
        var errors = invalidPage.GetProperty("body").GetProperty("errors");
        Assert.Equal("The cursor does not identify an existing station.", errors.GetProperty("AfterId")[0].GetString());
        Assert.Equal("The limit must be between 1 and 50.", errors.GetProperty("Limit")[0].GetString());

        await page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(
            new Uri(fixture.App.GetEndpoint("frontend", "http"), $"/stations/{stationId}").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "North Station", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Fictional lab site. Enter the synthetic public car park from Synthetic Avenue; "
            + "bays are beside the north exit."))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Type 2 · AC · 22 kW")).ToHaveCountAsync(2);
        await Expect(page.GetByText("EVSE 1 · Connector 1", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("EVSE 2 · Connector 1", new() { Exact = true })).ToBeVisibleAsync();
        var detail = await page.EvaluateAsync<JsonElement>(
            "async id => (await fetch('/api/stations/' + id)).json()", stationId.ToString());
        var charger = detail.GetProperty("chargers")[0];
        Assert.NotEqual(Guid.Empty, charger.GetProperty("id").GetGuid());
        foreach (var evse in charger.GetProperty("evses").EnumerateArray())
        {
            Assert.NotEqual(Guid.Empty, evse.GetProperty("id").GetGuid());
            Assert.Single(evse.GetProperty("connectors").EnumerateArray());
            Assert.NotEqual(Guid.Empty, evse.GetProperty("connectors")[0].GetProperty("id").GetGuid());
        }

        await Expect(page.GetByText("Time zone: Europe/London")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open in maps" })).ToHaveAttributeAsync(
            "href", "https://www.google.com/maps/search/?api=1&query=51.51%2C-0.12");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        await page.WaitForFunctionAsync(
            "Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"detail-{width}.png"), FullPage = true });

        await page.GotoAsync(
            new Uri(fixture.App.GetEndpoint("frontend", "http"), $"/stations/{Guid.NewGuid()}").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Station not found" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "All stations", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a station" })).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Browse_Operator_PublicCatalogueAndProtectedBoundaries()
    {
        using var client = fixture.App.CreateHttpClient("gateway", "http");
        using var anonymous = await client.GetAsync("/api/stations");
        Assert.Equal(System.Net.HttpStatusCode.OK, anonymous.StatusCode);
        using var write = await client.PostAsync("/api/stations", new StringContent("{}"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, write.StatusCode);
        using var privateDetail = await client.GetAsync($"/api/stations/{Guid.NewGuid()}/operations");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, privateDetail.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await LoginAsync(page, "operator");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a station" })).ToBeVisibleAsync();
        Assert.Equal(200, await page.EvaluateAsync<int>("async () => (await fetch('/api/stations')).status"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Operator area", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Operator area" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Account menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/stations")
            .AbsoluteUri);
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/operator").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to continue" })).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Browse_SessionStoreUnavailable_PublicContentRemainsUsable()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await LoginAsync(page, "operator");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Account menu" })).ToBeVisibleAsync();
        await using var database = new NpgsqlConnection(
            await fixture.App.GetConnectionStringAsync("gateway-auth-migration-db"));
        await database.OpenAsync();
        await using var revoke = new NpgsqlCommand(
            "REVOKE SELECT ON gateway_auth.sessions FROM gateway_auth_runtime", database);
        await revoke.ExecuteNonQueryAsync();
        try
        {
            await page.ReloadAsync();
            await Expect(page.GetByText("Account unavailable", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "North Station", Exact = true }))
                .ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Operator area", Exact = true }))
                .ToHaveCountAsync(0);
            Assert.Equal(
                200,
                await page.EvaluateAsync<int>(
                    "async () => (await fetch('/api/stations', {headers: {Authorization: 'Bearer forged'}})).status"));
            Assert.Equal(
                500,
                await page.EvaluateAsync<int>(
                    "async () => (await fetch('/api/diagnostics/instance')).status"));
            await page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Charging equipment" }))
                .ToBeVisibleAsync();
            await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/operator").AbsoluteUri);
            await Expect(page.GetByText("Unable to check your session.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Operator area" })).ToHaveCountAsync(0);
        }
        finally
        {
            await using var restore = new NpgsqlCommand(
                "GRANT SELECT ON gateway_auth.sessions TO gateway_auth_runtime", database);
            await restore.ExecuteNonQueryAsync();
        }

        await page.GetByRole(AriaRole.Banner).GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Operator area" })).ToBeVisibleAsync();
    }

    private async Task LoginAsync(IPage page, string username)
    {
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        string password = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == $"synthetic-{username}-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The synthetic user's password is required.");
        page.SetDefaultTimeout(30_000);
        await page.GotoAsync(
            new Uri(fixture.App.GetEndpoint("frontend", "http"), "/auth/login?returnUrl=/stations").AbsoluteUri);
        await page.GetByLabel("Username or email").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
    }
}
