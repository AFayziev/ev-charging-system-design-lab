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
        await LoginAsync(page, "operator");
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/operator").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit North Station", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit South Station", Exact = true }))
            .ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "Edit North Station", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Edit station" })).ToBeVisibleAsync();
        string id = page.Url.Split('/')[^2];
        var original = await EditorAsync(page, id);
        await using var database = new NpgsqlConnection(await fixture.App.GetConnectionStringAsync("platform-direct"));
        await database.OpenAsync();
        long before = await AuditCountAsync(database, id);
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
            Assert.Equal(before + 1, await AuditCountAsync(database, id));
            var publicDetail = await page.EvaluateAsync<JsonElement>(
                "async id => (await fetch('/api/stations/' + id)).json()", id);
            Assert.Equal("North Station Edited", publicDetail.GetProperty("name").GetString());
            Assert.Equal("3 Synthetic Avenue", publicDetail.GetProperty("address").GetString());
            Assert.Equal("Use the edited entrance.", publicDetail.GetProperty("accessInstructions").GetString());
            await using (var audit = new NpgsqlCommand(
                """
            SELECT a.details, a.actor_subject, a.occurred_at, a.actor_issuer, o.identity_issuer,
                   a.action, a.resource_type, a.actor_type, a.details_version, a.trace_id
            FROM charging.audit_entries a JOIN charging.stations s ON s.id = a.resource_id
            JOIN charging.operator_users o
                ON o.identity_issuer = a.actor_issuer AND o.identity_subject = a.actor_subject
            WHERE s.public_id = @id AND a.resource_type = 'Station' ORDER BY a.id DESC LIMIT 1
            """,
                database))
            {
                audit.Parameters.AddWithValue("id", Guid.Parse(id));
                await using var row = await audit.ExecuteReaderAsync();
                Assert.True(await row.ReadAsync());
                using var details = JsonDocument.Parse(row.GetString(0));
                var beforeDetails = details.RootElement.GetProperty("before");
                var afterDetails = details.RootElement.GetProperty("after");
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
                Assert.Equal("10000000-0000-0000-0000-000000000002", row.GetString(1));
                Assert.Equal(DateTimeKind.Utc, row.GetDateTime(2).Kind);
                Assert.Equal(row.GetString(4), row.GetString(3));
                Assert.Equal("Updated", row.GetString(5));
                Assert.Equal("Station", row.GetString(6));
                Assert.Equal("User", row.GetString(7));
                Assert.Equal(1, row.GetInt32(8));
                Assert.Equal(32, row.GetString(9).Length);
            }

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
            Assert.Equal(before + 1, await AuditCountAsync(database, id));
            Assert.Equal("North Station Edited", (await EditorAsync(page, id)).GetProperty("name").GetString());

            await page.GetByLabel("Station name").FillAsync("Retained failed draft");
            await using var revoke = new NpgsqlCommand(
                "REVOKE INSERT ON charging.audit_entries FROM platform_writer", database);
            await revoke.ExecuteNonQueryAsync();
            try
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Changes not confirmed");
                await Expect(page.GetByLabel("Station name"))
                    .ToHaveValueAsync("Retained failed draft");
                Assert.Equal(before + 1, await AuditCountAsync(database, id));
                Assert.Equal("North Station Edited", (await EditorAsync(page, id)).GetProperty("name").GetString());
            }
            finally
            {
                await using var restore = new NpgsqlCommand(
                    "GRANT INSERT ON charging.audit_entries TO platform_writer", database);
                await restore.ExecuteNonQueryAsync();
            }

            await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
            await Expect(page.GetByText("Changes saved.", new() { Exact = true })).ToBeVisibleAsync();
            Assert.Equal(before + 2, await AuditCountAsync(database, id));
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
            Assert.Equal(before + 3, await AuditCountAsync(database, id));
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
            Assert.Equal(0, await AuditCountAsync(database, southId));
            await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), $"/stations/{southId}/edit")
                .AbsoluteUri);
            await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Access denied");
        }
        finally
        {
            await using var restoreContent = new NpgsqlCommand(
                """
                UPDATE charging.stations SET name = @name, address = @address,
                    access_instructions = @instructions WHERE public_id = @id
                """,
                database);
            restoreContent.Parameters.AddWithValue("id", Guid.Parse(id));
            restoreContent.Parameters.AddWithValue("name", original.GetProperty("name").GetString()!);
            restoreContent.Parameters.AddWithValue("address", original.GetProperty("address").GetString()!);
            restoreContent.Parameters
                .AddWithValue("instructions", original.GetProperty("accessInstructions").GetString()!);
            await restoreContent.ExecuteNonQueryAsync();
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
        await LoginAsync(page, "driver");
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

    private static async Task<long> AuditCountAsync(NpgsqlConnection database, string id)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM charging.audit_entries a
            JOIN charging.stations s ON s.id = a.resource_id
            WHERE s.public_id = @id AND a.resource_type = 'Station'
            """,
            database);
        command.Parameters.AddWithValue("id", Guid.Parse(id));
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task LoginAsync(IPage page, string username)
    {
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        string password = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == $"synthetic-{username}-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The synthetic user's password is required.");
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/auth/login?returnUrl=/stations")
            .AbsoluteUri);
        await page.GetByLabel("Username or email").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Account menu" })).ToBeVisibleAsync();
    }
}
