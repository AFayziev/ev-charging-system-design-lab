using System.Text.Json;
using Aspire.Hosting.Testing;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class StationEditingTests(AppHostFixture fixture)
{
    [Fact]
    [Trait("Category", "Browser")]
    public async Task Edit_AssignedOperator_EnforcesAuditedChanges()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        await fixture.LoginAsync(page, "operator");
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/operator").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit North Station", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit South Station", Exact = true }))
            .ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "Edit North Station", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Edit station" })).ToBeVisibleAsync();
        string id = page.Url.Split('/')[^2];
        var original = await EditorAsync(page, id);
        await using var storage = await fixture.CreatePlatformDbContextAsync();
        Guid publicId = Guid.Parse(id);
        Guid stationId = await storage.Stations.Where(value => value.PublicId == publicId)
            .Select(value => value.Id)
            .SingleAsync();
        long before = await AuditCountAsync(storage, stationId);
        try
        {
            await page.GetByLabel("Station name").FillAsync("North Station Edited");
            await page.GetByLabel("Address").FillAsync("3 Synthetic Avenue");
            await page.GetByLabel("Access instructions", new() { Exact = true }).FillAsync("Use the edited entrance.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
            await Expect(page.GetByText("Changes saved.", new() { Exact = true })).ToBeVisibleAsync();
            var saved = await EditorAsync(page, id);
            Assert.Equal("North Station Edited", saved.GetProperty("name").GetString());
            Assert.NotEqual(original.GetProperty("version").GetUInt32(), saved.GetProperty("version").GetUInt32());
            Assert.Equal(before + 1, await AuditCountAsync(storage, stationId));
            var publicDetail = await page.EvaluateAsync<JsonElement>(
                "async id => (await fetch('/api/stations/' + id)).json()", id);
            Assert.Equal("North Station Edited", publicDetail.GetProperty("name").GetString());
            Assert.Equal("3 Synthetic Avenue", publicDetail.GetProperty("address").GetString());
            Assert.Equal("Use the edited entrance.", publicDetail.GetProperty("accessInstructions").GetString());
            var audit = await storage.AuditEntries.Where(value => value.ResourceId == stationId
                && value.ResourceType == AuditResourceType.Station)
                .OrderByDescending(value => value.Id)
                .FirstAsync();
            var beforeDetails = audit.Details.GetProperty("before");
            var afterDetails = audit.Details.GetProperty("after");
            Assert.Equal(3, beforeDetails.EnumerateObject().Count());
            Assert.Equal(3, afterDetails.EnumerateObject().Count());
            Assert.Equal(original.GetProperty("name").GetString(), beforeDetails.GetProperty("name").GetString());
            Assert.Equal("North Station Edited", afterDetails.GetProperty("name").GetString());
            Assert.Equal(
                original.GetProperty("address").GetString(),
                beforeDetails.GetProperty("address").GetString());
            Assert.Equal("3 Synthetic Avenue", afterDetails.GetProperty("address").GetString());
            Assert.Equal(
                original.GetProperty("accessInstructions").GetString(),
                beforeDetails.GetProperty("accessInstructions").GetString());
            Assert.Equal("Use the edited entrance.", afterDetails.GetProperty("accessInstructions").GetString());
            Assert.Equal("10000000-0000-0000-0000-000000000002", audit.ActorSubject);
            Assert.Equal(TimeSpan.Zero, audit.OccurredAt.Offset);
            Assert.True(await storage.OperatorUsers.AnyAsync(value => value.IdentityIssuer == audit.ActorIssuer
                && value.IdentitySubject == audit.ActorSubject));
            Assert.Equal(AuditAction.Updated, audit.Action);
            Assert.Equal(AuditResourceType.Station, audit.ResourceType);
            Assert.Equal(AuditActorType.User, audit.ActorType);
            Assert.Equal(1, audit.DetailsVersion);
            Assert.Equal(32, audit.TraceId!.Length);

            Assert.Equal(409, await PutAsync(page, id, original));
            var staleSameValues = new
            {
                version = original.GetProperty("version").GetUInt32(),
                name = saved.GetProperty("name").GetString(),
                address = saved.GetProperty("address").GetString(),
                accessInstructions = saved.GetProperty("accessInstructions").GetString()
            };
            Assert.Equal(409, await PutAsync(page, id, JsonSerializer.SerializeToElement(staleSameValues)));
            var invalid = new
            {
                version = saved.GetProperty("version").GetUInt32(),
                name = "   ",
                address = "Valid address",
                accessInstructions = ""
            };
            Assert.Equal(400, await PutAsync(page, id, JsonSerializer.SerializeToElement(invalid)));
            Assert.Equal(before + 1, await AuditCountAsync(storage, stationId));
            Assert.Equal("North Station Edited", (await EditorAsync(page, id)).GetProperty("name").GetString());

            await page.SetViewportSizeAsync(390, 844);
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
            string screenshots = Path.Combine(AppContext.BaseDirectory, "station-screenshots");
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(
                new() { Path = Path.Combine(screenshots, "editor-mobile.png"), FullPage = true });
            await page.SetViewportSizeAsync(1280, 900);
            await page.ScreenshotAsync(
                new() { Path = Path.Combine(screenshots, "editor-desktop.png"), FullPage = true });

            var sameValues = await EditorAsync(page, id);
            Assert.Equal(200, await PutAsync(page, id, sameValues));
            Assert.Equal(before + 2, await AuditCountAsync(storage, stationId));
            var sameValueResult = await EditorAsync(page, id);
            Assert.Equal(sameValues.GetProperty("name").GetString(), sameValueResult.GetProperty("name").GetString());
            Assert.NotEqual(
                sameValues.GetProperty("version").GetUInt32(),
                sameValueResult.GetProperty("version").GetUInt32());

            var south = await page.EvaluateAsync<JsonElement>(
                "async () => (await fetch('/api/stations')).json()");
            string southId = south.GetProperty("stations").EnumerateArray()
                .Single(station => station.GetProperty("name").GetString() == "South Station")
                .GetProperty("id")
                .GetString()!;
            Assert.Equal(
                403,
                await page.EvaluateAsync<int>(
                    "async id => (await fetch('/api/stations/' + id + '/edit')).status", southId));
            Assert.Equal(403, await PutAsync(page, southId, saved));
            Guid southPublicId = Guid.Parse(southId);
            Guid southStationId = await storage.Stations.Where(value => value.PublicId == southPublicId)
                .Select(value => value.Id)
                .SingleAsync();
            Assert.Equal(0, await AuditCountAsync(storage, southStationId));
            await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), $"/stations/{southId}/edit")
                .AbsoluteUri);
            await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Access denied");
        }
        finally
        {
            string name = original.GetProperty("name").GetString()!;
            string address = original.GetProperty("address").GetString()!;
            string instructions = original.GetProperty("accessInstructions").GetString()!;
            await storage.Stations.Where(value => value.PublicId == publicId).ExecuteUpdateAsync(update => update
                .SetProperty(value => value.Name, name)
                .SetProperty(value => value.Address, address)
                .SetProperty(value => value.AccessInstructions, instructions));
        }
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Edit_Driver_DeniesAssignedListEditorAndWrite()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        await fixture.LoginAsync(page, "driver");
        var stations = await page.EvaluateAsync<JsonElement>("async () => (await fetch('/api/stations')).json()");
        string id = stations.GetProperty("stations")[0].GetProperty("id").GetString()!;
        Assert.Equal(
            403,
            await page.EvaluateAsync<int>(
                "async () => (await fetch('/api/stations/assigned')).status"));
        Assert.Equal(
            403,
            await page.EvaluateAsync<int>(
                "async id => (await fetch('/api/stations/' + id + '/edit')).status", id));
        var payload = new
        {
            version = 1,
            name = "Unauthorized",
            address = "Unauthorized",
            accessInstructions = ""
        };
        Assert.Equal(403, await PutAsync(page, id, JsonSerializer.SerializeToElement(payload)));
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), $"/stations/{id}/edit")
            .AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access denied" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private static Task<JsonElement> EditorAsync(IPage page, string id) => page.EvaluateAsync<JsonElement>(
        "async id => (await fetch('/api/stations/' + id + '/edit')).json()", id);

    private static Task<int> PutAsync(IPage page, string id, JsonElement payload) => page.EvaluateAsync<int>(
        """
        async ({ id, payload }) => {
            const session = await (await fetch('/auth/session')).json();
            return (await fetch('/api/stations/' + id, {
                method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.antiforgeryToken },
                body: payload
            })).status;
        }
        """,
        new { id, payload = payload.GetRawText() });

    private static Task<int> AuditCountAsync(PlatformDbContext database, Guid stationId) => database.AuditEntries
        .CountAsync(value => value.ResourceId == stationId && value.ResourceType == AuditResourceType.Station);
}
