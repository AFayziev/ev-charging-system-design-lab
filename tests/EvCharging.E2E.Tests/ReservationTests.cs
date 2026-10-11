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
public sealed class ReservationTests(AppHostFixture fixture)
{
    [Fact]
    [Trait("Category", "Browser")]
    public async Task Book_Driver_PersistsOwnedBookingAndHandlesFailures()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        page.SetDefaultTimeout(30_000);
        await LoginAsync(page, "driver");
        await page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true }).ClickAsync();
        string stationUrl = page.Url;
        await page.GetByRole(AriaRole.Radio, new() { Name = "EVSE", Exact = false }).First.ClickAsync();
        string bookingUrl = page.Url;
        var startsAt = DateTimeOffset.UtcNow.AddDays(10);
        var endsAt = startsAt.AddHours(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        string startInput = TimeZoneInfo.ConvertTime(startsAt, zone).ToString(
            "yyyy-MM-ddTHH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        string endInput = TimeZoneInfo.ConvertTime(endsAt, zone).ToString(
            "yyyy-MM-ddTHH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        await PickTimeAsync(page, "Start", startInput);
        await PickTimeAsync(page, "End", startInput);
        string earliestEnd = DateTime.Parse(startInput, System.Globalization.CultureInfo.InvariantCulture)
            .AddMinutes(1)
            .ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "End time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(earliestEnd));
        await PickTimeAsync(page, "End", endInput);
        await page.GetByRole(AriaRole.Radio, new() { Name = "EVSE", Exact = false }).Last.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Connector" }))
            .ToHaveValueAsync("EVSE 2 · Connector 1 (Type 2, 22 kW)");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(startInput));
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "End time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(endInput));
        await page.GetByRole(AriaRole.Radio, new() { Name = "EVSE", Exact = false }).First.ClickAsync();
        await page.GetByText("Review resolved times", new() { Exact = true }).ClickAsync();
        await Expect(page.GetByText("Resolved browser time:", new() { Exact = false })).ToBeVisibleAsync();
        string browserPreview = await page.GetByText("Resolved browser time:", new() { Exact = false })
            .InnerTextAsync();
        string stationStart = await page.GetByText("Start:", new() { Exact = false }).InnerTextAsync();
        string stationEnd = await page.GetByText("End:", new() { Exact = false }).InnerTextAsync();
        Assert.Contains(stationStart["Start: ".Length..], browserPreview, StringComparison.Ordinal);
        Assert.Contains(stationEnd["End: ".Length..], browserPreview, StringComparison.Ordinal);
        await page.GetByText("Review resolved times", new() { Exact = true }).ClickAsync();
        await CaptureAsync(page, "booking");
        await CaptureAsync(page, "booking-picker", openPicker: true);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reserve connector" }).ClickAsync();
        await Expect(page.GetByText("Reservation created.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/reservations$"));
        var ownedBookings = await GetAsync(page, "/api/reservations");
        string id = ownedBookings
            .GetProperty("reservations")
            .EnumerateArray()
            .Single()
            .GetProperty("id")
            .GetString()!;
        await Expect(page.GetByRole(AriaRole.Article, new() { Name = "North Station reservation" }))
            .ToHaveAttributeAsync("data-highlighted", "true");
        Assert.Equal(4, Guid.Parse(id).Version);
        var detail = await GetAsync(page, $"/api/reservations/{id}");
        Assert.Equal("active", detail.GetProperty("state").GetString());
        Assert.Equal("North Station", detail.GetProperty("stationName").GetString());
        Assert.Equal(
            "1 Synthetic Avenue, Demo North Quarter, London",
            detail.GetProperty("stationAddress").GetString());
        Assert.Equal("AC", detail.GetProperty("powerSupply").GetString());
        Assert.Equal(22m, detail.GetProperty("ratedPowerKW").GetDecimal());
        string connectorId = detail.GetProperty("connectorId").GetString()!;
        var start = detail.GetProperty("startsAt").GetDateTimeOffset();
        var end = detail.GetProperty("endsAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.ParseExact(
                    startInput,
                    "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture),
                zone),
            start.UtcDateTime);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.ParseExact(
                    endInput,
                    "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture),
                zone),
            end.UtcDateTime);
        Assert.Equal(TimeSpan.FromHours(1), end - start);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My reservations", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "North Station", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("1 Synthetic Avenue, Demo North Quarter, London", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Type 2 · AC · 22 kW", new() { Exact = true })).ToBeVisibleAsync();
        await CaptureAsync(page, "reservation-list");
        await page.ReloadAsync();
        await Expect(page.GetByText("Reservation created.", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Article, new() { Name = "North Station reservation" }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Article, new() { Name = "North Station reservation" }))
            .Not
            .ToHaveAttributeAsync("data-highlighted", "true");
        Assert.Equal(409, (await PostAsync(page, connectorId, start, end)).GetProperty("status").GetInt32());
        Assert.Equal(400, (await PostAsync(page, connectorId, end, start)).GetProperty("status").GetInt32());
        Assert.Equal(
            400,
            (await PostAsync(
                page,
                connectorId,
                DateTimeOffset.UtcNow.AddHours(-2),
                DateTimeOffset.UtcNow.AddHours(-1)))
                .GetProperty("status")
                .GetInt32());
        Assert.Equal(201, (await PostAsync(page, connectorId, end, end.AddHours(1))).GetProperty("status").GetInt32());
        Assert.Equal(
            201,
            (await PostAsync(
                page,
                connectorId,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(5)))
                .GetProperty("status")
                .GetInt32());

        var competitors = await page.EvaluateAsync<JsonElement>(
            """
            async ({ connectorId, startsAt, endsAt }) => {
                const session = await (await fetch('/auth/session')).json();
                const submit = async () => (await fetch('/api/reservations', {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.antiforgeryToken },
                    body: JSON.stringify({ connectorId, startsAt, endsAt })
                })).status;
                return Promise.all(Array.from({ length: 8 }, submit));
            }
            """,
            new
            {
                connectorId,
                startsAt = end.AddHours(3).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                endsAt = end.AddHours(4).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            });
        var statuses = competitors.EnumerateArray().Select(status => status.GetInt32()).ToArray();
        Assert.Equal(1, statuses.Count(status => status == 201));
        Assert.Equal(7, statuses.Count(status => status == 409));

        await using var database = new NpgsqlConnection(await fixture.App.GetConnectionStringAsync("platform-direct"));
        await database.OpenAsync();
        await using var ownedAudit = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM charging.audit_entries a JOIN charging.reservations r ON r.id = a.resource_id
            JOIN charging.drivers d ON d.id = r.driver_id
            WHERE r.public_id = @id AND a.resource_type = 'Reservation' AND a.action = 'Added'
              AND a.actor_issuer = d.identity_issuer AND a.actor_subject = d.identity_subject
            """,
            database);
        ownedAudit.Parameters.AddWithValue("id", Guid.Parse(id));
        Assert.Equal(1L, await ownedAudit.ExecuteScalarAsync());
        await using var foreign = new NpgsqlCommand(
            """
            WITH driver AS (
                INSERT INTO charging.drivers (id, identity_issuer, identity_subject)
                SELECT uuidv7(), identity_issuer, @subject FROM charging.drivers LIMIT 1 RETURNING id
            )
            INSERT INTO charging.reservations (id, driver_id, connector_id, starts_at, ends_at, state, created_at)
            SELECT uuidv7(), d.id, c.id, @start, @end, 0, now() FROM driver d CROSS JOIN charging.connectors c
            WHERE c.public_id = @connector RETURNING public_id
            """,
            database);
        foreign.Parameters.AddWithValue("subject", Guid.NewGuid().ToString());
        foreign.Parameters.AddWithValue("connector", Guid.Parse(connectorId));
        foreign.Parameters.AddWithValue("start", end.AddDays(2));
        foreign.Parameters.AddWithValue("end", end.AddDays(2).AddHours(1));
        var foreignId = Assert.IsType<Guid>(await foreign.ExecuteScalarAsync());
        Assert.Equal(403, await StatusAsync(page, $"/api/reservations/{foreignId}"));
        Assert.Equal(400, await StatusAsync(page, $"/api/reservations?afterId={foreignId}"));
        var list = await GetAsync(page, "/api/reservations");
        Assert.DoesNotContain(
            list.GetProperty("reservations").EnumerateArray(),
            reservation => reservation.GetProperty("id").GetGuid() == foreignId);

        await using var creationTimes = new NpgsqlCommand(
            """
            UPDATE charging.reservations
            SET created_at = CASE WHEN public_id = @first THEN @latest ELSE @earlier END
            WHERE driver_id = (SELECT driver_id FROM charging.reservations WHERE public_id = @first)
            """,
            database);
        creationTimes.Parameters.AddWithValue("first", Guid.Parse(id));
        creationTimes.Parameters.AddWithValue("latest", DateTimeOffset.UtcNow);
        creationTimes.Parameters.AddWithValue("earlier", DateTimeOffset.UtcNow.AddHours(-1));
        await creationTimes.ExecuteNonQueryAsync();

        await using var expectedOrder = new NpgsqlCommand(
            """
            SELECT public_id FROM charging.reservations
            WHERE driver_id = (SELECT driver_id FROM charging.reservations WHERE public_id = @first)
              AND public_id <> @first
            ORDER BY id DESC
            """,
            database);
        expectedOrder.Parameters.AddWithValue("first", Guid.Parse(id));
        var expected = new List<Guid> { Guid.Parse(id) };
        await using (var rows = await expectedOrder.ExecuteReaderAsync())
        {
            while (await rows.ReadAsync())
            {
                expected.Add(rows.GetGuid(0));
            }
        }

        var paged = new List<Guid>();
        string path = "/api/reservations?limit=2";
        do
        {
            var batch = await GetAsync(page, path);
            paged.AddRange(batch.GetProperty("reservations")
                .EnumerateArray()
                .Select(reservation => reservation.GetProperty("id").GetGuid()));
            Assert.True(paged.Count <= expected.Count, "Reservation paging repeated records.");
            path = batch.GetProperty("nextAfterId").ValueKind == JsonValueKind.Null
                ? ""
                : $"/api/reservations?limit=2&afterId={batch.GetProperty("nextAfterId").GetGuid()}";
        }
        while (path.Length > 0);

        Assert.Equal(expected, paged);

        await page.GotoAsync(bookingUrl);
        await PickTimeAsync(page, "Start", startInput);
        await PickTimeAsync(page, "End", endInput);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reserve connector" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("already reserved");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(startInput));
        string failedStart = TimeZoneInfo.ConvertTime(end.AddDays(3), zone).ToString(
            "yyyy-MM-ddTHH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        string failedEnd = TimeZoneInfo.ConvertTime(end.AddDays(3).AddHours(1), zone).ToString(
            "yyyy-MM-ddTHH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        await PickTimeAsync(page, "Start", failedStart);
        await PickTimeAsync(page, "End", failedEnd);
        await using var revoke = new NpgsqlCommand(
            "REVOKE INSERT ON charging.audit_entries FROM platform_writer",
            database);
        await revoke.ExecuteNonQueryAsync();
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Reserve connector" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Booking not confirmed");
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start time", Exact = true }))
                .ToHaveTextAsync(DisplayTime(failedStart));
            await using var rolledBack = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM charging.reservations
                WHERE connector_id = (SELECT id FROM charging.connectors WHERE public_id = @id)
                  AND starts_at = @start
                """,
                database);
            rolledBack.Parameters.AddWithValue("id", Guid.Parse(connectorId));
            rolledBack.Parameters.AddWithValue("start", end.AddDays(3));
            Assert.Equal(0L, await rolledBack.ExecuteScalarAsync());
        }
        finally
        {
            await using var restore = new NpgsqlCommand(
                "GRANT INSERT ON charging.audit_entries TO platform_writer",
                database);
            await restore.ExecuteNonQueryAsync();
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Reserve connector" }).ClickAsync();
        await Expect(page.GetByText("Reservation created.", new() { Exact = true })).ToBeVisibleAsync();
        await page.GotoAsync(stationUrl);
        Assert.Equal(200, await StatusAsync(page, "/api/reservations"));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Book_AnonymousOrOperator_DeniesDriverRoutes()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(fixture.App.GetEndpoint("frontend", "http").AbsoluteUri);
        Assert.Equal(401, await StatusAsync(page, "/api/reservations"));
        await LoginAsync(page, "operator");
        Assert.Equal(403, await StatusAsync(page, "/api/reservations"));
        Assert.Equal(403, await StatusAsync(page, $"/api/reservations/{Guid.NewGuid()}"));
        Assert.Equal(
            403,
            (await PostAsync(
                page,
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1)))
                .GetProperty("status")
                .GetInt32());
    }

    private static Task<JsonElement> GetAsync(IPage page, string path) => page.EvaluateAsync<JsonElement>(
        "async path => (await fetch(path)).json()", path);

    private static Task<int> StatusAsync(IPage page, string path) => page.EvaluateAsync<int>(
        "async path => (await fetch(path)).status", path);

    private static Task<JsonElement> PostAsync(
        IPage page,
        string connectorId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        page.EvaluateAsync<JsonElement>(
            """
            async booking => {
                const session = await (await fetch('/auth/session')).json();
                const response = await fetch('/api/reservations', {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.antiforgeryToken },
                    body: JSON.stringify(booking)
                });
                return { status: response.status, body: await response.json() };
            }
            """,
            new
            {
                connectorId,
                startsAt = startsAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                endsAt = endsAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            });

    private static string DisplayTime(string value) =>
        DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task PickTimeAsync(IPage page, string label, string value)
    {
        var time = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        await page.GetByRole(AriaRole.Button, new() { Name = $"{label} time", Exact = true }).ClickAsync();
        var picker = page.GetByRole(AriaRole.Dialog).Filter(new()
        {
            Has = page.GetByRole(AriaRole.Button, new() { Name = $"Confirm {label.ToLowerInvariant()} time" })
        });
        await Expect(picker).ToBeVisibleAsync();
        var day = picker.GetByRole(
            AriaRole.Button,
            new()
            {
                Name = time.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Exact = true
            });
        if (await day.CountAsync() == 0)
        {
            await picker.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        }

        await day.ClickAsync();
        await picker.GetByRole(AriaRole.Spinbutton, new() { Name = $"{label} hours" })
            .FillAsync(time.ToString("HH", System.Globalization.CultureInfo.InvariantCulture));
        await picker.GetByRole(AriaRole.Spinbutton, new() { Name = $"{label} minutes" })
            .FillAsync(time.ToString("mm", System.Globalization.CultureInfo.InvariantCulture));
        await picker.GetByRole(AriaRole.Button, new() { Name = $"Confirm {label.ToLowerInvariant()} time" })
            .ClickAsync();
    }

    private static async Task CaptureAsync(IPage page, string name, bool openPicker = false)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "reservation-screenshots");
        Directory.CreateDirectory(directory);
        foreach (var viewport in new[] { ("desktop", 1280, 900), ("mobile", 390, 844) })
        {
            await page.SetViewportSizeAsync(viewport.Item2, viewport.Item3);
            if (openPicker)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Start time", Exact = true }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
                if (viewport.Item1 == "mobile")
                {
                    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Choose start date and time" }))
                        .ToBeVisibleAsync();
                }
            }

            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
            await page.ScreenshotAsync(
                new()
                {
                    Path = Path.Combine(directory, $"{name}-{viewport.Item1}.png"),
                    FullPage = !openPicker,
                    Animations = ScreenshotAnimations.Disabled
                });
            if (openPicker)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Confirm start time" }).ClickAsync();
                await Expect(page.GetByRole(AriaRole.Dialog)).ToBeHiddenAsync();
            }
        }
    }

    private async Task LoginAsync(IPage page, string username)
    {
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        string password = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == $"synthetic-{username}-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The synthetic user's password is required.");
        await page.GotoAsync(
            new Uri(fixture.App.GetEndpoint("frontend", "http"), "/auth/login?returnUrl=/stations").AbsoluteUri);
        await page.GetByLabel("Username or email").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Account menu" })).ToBeVisibleAsync();
    }
}
