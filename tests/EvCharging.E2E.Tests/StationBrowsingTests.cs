using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
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
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "South Station", Exact = true }))
            .ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        await page.WaitForFunctionAsync(
            "Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)");
        string screenshots = Path.Combine(AppContext.BaseDirectory, "station-screenshots");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"catalog-{width}.png"), FullPage = true });

        var stationLink = page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true });
        string stationPath = await stationLink.GetAttributeAsync("href")
            ?? throw new InvalidOperationException("The station link is required.");
        await stationLink.ClickAsync();
        await Expect(page).ToHaveURLAsync(
            new Uri(fixture.App.GetEndpoint("frontend", "http"), stationPath).AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "North Station", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Fictional lab site. Enter the synthetic public car park from Synthetic Avenue; "
            + "bays are beside the north exit."))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Type 2 · AC · 22 kW")).ToHaveCountAsync(2);
        await Expect(page.GetByText("Connector 1", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Connector 2", new() { Exact = true })).ToBeVisibleAsync();
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
    [Trait("Category", "Api")]
    public async Task Browse_PublicCatalogue_PagesAndValidatesRequests()
    {
        using var client = fixture.App.CreateHttpClient("gateway", "http");
        var first = await client.GetFromJsonAsync<JsonElement>("/api/stations?limit=1");
        var station = first.GetProperty("stations")[0];
        Assert.Equal("North Station", station.GetProperty("name").GetString());
        Assert.Equal(2, station.GetProperty("connectorCount").GetInt32());
        Assert.Equal(1, station.GetProperty("chargerCount").GetInt32());
        var stationId = station.GetProperty("id").GetGuid();
        string cursor = first.GetProperty("nextAfterId").GetString()!;
        Assert.Equal(stationId.ToString(), cursor);
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/stations?limit=1&afterId={cursor}");
        Assert.Equal("South Station", second.GetProperty("stations")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, second.GetProperty("nextAfterId").ValueKind);
        string nextCursor = second.GetProperty("nextAfterId").GetString()!;
        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/stations?afterId={nextCursor}");
        var remainingStations = remaining.GetProperty("stations").EnumerateArray().ToArray();
        Assert.NotEmpty(remainingStations);
        Assert.Equal(JsonValueKind.Null, remaining.GetProperty("nextAfterId").ValueKind);
        Guid lastId = remainingStations[^1].GetProperty("id").GetGuid();
        var empty = await client.GetFromJsonAsync<JsonElement>($"/api/stations?afterId={lastId}");
        Assert.Empty(empty.GetProperty("stations").EnumerateArray());
        using var invalidPage = await client.GetAsync($"/api/stations?afterId={Guid.NewGuid()}&limit=51");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Equal("application/problem+json", invalidPage.Content.Headers.ContentType?.MediaType);
        var invalidBody = await invalidPage.Content.ReadFromJsonAsync<JsonElement>();
        var errors = invalidBody.GetProperty("errors");
        Assert.Equal("The cursor does not identify an existing station.", errors.GetProperty("AfterId")[0].GetString());
        Assert.Equal("The limit must be between 1 and 50.", errors.GetProperty("Limit")[0].GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/stations/{stationId}");
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        var charger = detail.GetProperty("chargers")[0];
        Assert.NotEqual(Guid.Empty, charger.GetProperty("id").GetGuid());
        var connectors = charger.GetProperty("connectors").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2 }, connectors.Select(connector => connector.GetProperty("number").GetInt32()));
        Assert.All(connectors, connector => Assert.NotEqual(Guid.Empty, connector.GetProperty("id").GetGuid()));

    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Search_Anonymous_FindsSeededStationsThroughTheGateway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync("search-api", timeout.Token);
        using var client = fixture.App.CreateHttpClient("gateway", "http");

        // The search worker builds the index after startup, so the first answers can be the typed unavailable error.
        JsonElement result;
        while (true)
        {
            using var response = await client.GetAsync(
                "/api/search/stations?q=north&connectorType=Type2",
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                result = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
                if (result.GetProperty("stations").GetArrayLength() > 0)
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        }

        var station = result.GetProperty("stations")[0];
        Assert.Equal("North Station", station.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, station.GetProperty("distanceKm").ValueKind);
        var browse = await client.GetFromJsonAsync<JsonElement>("/api/stations?limit=50", timeout.Token);
        Assert.Contains(
            browse.GetProperty("stations").EnumerateArray(),
            browsed => browsed.GetProperty("id").GetGuid() == station.GetProperty("id").GetGuid());

        var firstPage = await client.GetFromJsonAsync<JsonElement>("/api/search/stations?limit=1", timeout.Token);
        string cursor = firstPage.GetProperty("nextCursor").GetString()!;
        var secondPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/search/stations?cursor={cursor}&limit=1", timeout.Token);
        Assert.NotEqual(
            firstPage.GetProperty("stations")[0].GetProperty("id").GetGuid(),
            secondPage.GetProperty("stations")[0].GetProperty("id").GetGuid());

        using var invalid = await client.GetAsync("/api/search/stations?lat=95&lon=0", timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var write = await client.PostAsync("/api/search/stations", new StringContent("{}"), timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Browse_Operator_PublicCatalogueAndProtectedBoundaries()
    {
        using var authenticationHost = await fixture.CreateAuthenticationHostAsync();
        var contexts = authenticationHost.Services.GetRequiredService<IDbContextFactory<AuthenticationDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        var existingSessionIds = await database.Sessions.Select(value => value.Id).ToListAsync();
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
        await fixture.LoginAsync(page, "operator");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a station" })).ToBeVisibleAsync();
        Assert.Equal(200, await page.EvaluateAsync<int>("async () => (await fetch('/api/stations')).status"));
        Guid sessionId = await database.Sessions.AsNoTracking()
            .Where(value => !existingSessionIds.Contains(value.Id))
            .Select(value => value.Id)
            .SingleAsync();
        var tickets = authenticationHost.Services.GetRequiredService<ITicketStore>();
        var ticket = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(sessionId.ToString()));
        var stations = await client.GetFromJsonAsync<JsonElement>("/api/stations?limit=2");
        var northId = stations.GetProperty("stations")[0].GetProperty("id").GetGuid();
        var southId = stations.GetProperty("stations")[1].GetProperty("id").GetGuid();
        using var api = fixture.App.CreateHttpClient("api", "http");
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ticket.Properties.GetTokenValue("access_token"));
        var assignedStation = await api.GetFromJsonAsync<JsonElement>($"/stations/{northId}");
        Assert.True(assignedStation.GetProperty("canEdit").GetBoolean());
        var unassignedStation = await api.GetFromJsonAsync<JsonElement>($"/stations/{southId}");
        Assert.False(unassignedStation.GetProperty("canEdit").GetBoolean());
        Assert.False(await page.EvaluateAsync<bool>(
            "async id => (await (await fetch(`/api/stations/${id}`)).json()).canEdit", northId.ToString()));
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
        await fixture.LoginAsync(page, "operator");
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
                    "async () => (await fetch('/api/stations/assigned')).status"));
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

}
