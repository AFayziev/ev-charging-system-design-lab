using System.Globalization;
using System.Text.Json;
using Aspire.Hosting.Testing;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class ReservationTests(AppHostFixture fixture) : IAsyncLifetime
{
    private PlatformDbContext? _storage;
    private Driver? _driver;
    private readonly List<Guid> _additionalDrivers = [];

    private PlatformDbContext Storage => _storage ?? throw new InvalidOperationException("Storage is not initialized.");
    private Driver Driver => _driver ?? throw new InvalidOperationException("The driver is not initialized.");

    public async Task InitializeAsync()
    {
        _storage = await fixture.CreatePlatformDbContextAsync();
        _driver = await Storage.Drivers.SingleAsync();
    }

    public async Task DisposeAsync()
    {
        if (_storage is null)
        {
            return;
        }

        try
        {
            if (_driver is not null)
            {
                var driverIds = _additionalDrivers.Append(_driver.Id).ToArray();
                var reservationIds = Storage.Reservations.Where(value => driverIds.Contains(value.DriverId))
                    .Select(value => value.Id);
                await Storage.Sessions.Where(value => reservationIds.Contains(value.ReservationId))
                    .ExecuteDeleteAsync();
                await Storage.Reservations.Where(value => driverIds.Contains(value.DriverId)).ExecuteDeleteAsync();
                await Storage.Drivers.Where(value => _additionalDrivers.Contains(value.Id)).ExecuteDeleteAsync();
            }
        }
        finally
        {
            await _storage.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Book_Driver_CreatesAuditedBookingAndClearsFeedbackOnReload()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        await fixture.LoginAsync(page, "driver");
        await page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Radio, new() { Name = "EVSE", Exact = false }).First.ClickAsync();
        var policy = await GetAsync(page, "/api/reservations/booking-policy");
        Assert.Equal(
            AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver,
            policy.GetProperty("maximumOutstandingReservationsPerDriver").GetInt32());
        Assert.Equal(
            AppHostFixture.BookingLimits.MaximumBookingDuration.TotalSeconds,
            policy.GetProperty("maximumBookingDurationSeconds").GetDouble());
        Assert.Equal(
            AppHostFixture.BookingLimits.MaximumAdvanceBookingHorizon.TotalSeconds,
            policy.GetProperty("maximumAdvanceBookingHorizonSeconds").GetDouble());
        await Expect(page.GetByText("Up to 4 outstanding reservations.", new() { Exact = false }))
            .ToContainTextAsync("Slots up to 3 hours; book up to 20 days ahead.");
        var startsAt = DateTimeOffset.UtcNow.AddDays(10);
        var endsAt = startsAt.AddHours(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        string startInput = TimeZoneInfo.ConvertTime(startsAt, zone).ToString(
            "yyyy-MM-ddTHH:mm",
            CultureInfo.InvariantCulture);
        string endInput = TimeZoneInfo.ConvertTime(endsAt, zone).ToString(
            "yyyy-MM-ddTHH:mm",
            CultureInfo.InvariantCulture);
        await PickTimeAsync(page, "Start", startInput);
        await PickTimeAsync(page, "End", startInput);
        string earliestEnd = DateTime.Parse(startInput, CultureInfo.InvariantCulture)
            .AddMinutes(1)
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
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
        var start = detail.GetProperty("startsAt").GetDateTimeOffset();
        var end = detail.GetProperty("endsAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.ParseExact(
                    startInput,
                    "yyyy-MM-ddTHH:mm",
                    CultureInfo.InvariantCulture),
                zone),
            start.UtcDateTime);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.ParseExact(
                    endInput,
                    "yyyy-MM-ddTHH:mm",
                    CultureInfo.InvariantCulture),
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
        var accepted = await Storage.Reservations.Include(reservation => reservation.Driver)
            .SingleAsync(reservation => reservation.PublicId == Guid.Parse(id));
        var audit = await Storage.AuditEntries.SingleAsync(entry => entry.ResourceId == accepted.Id
            && entry.ResourceType == AuditResourceType.Reservation && entry.Action == AuditAction.Added);
        Assert.Equal(accepted.Driver!.IdentityIssuer, audit.ActorIssuer);
        Assert.Equal(accepted.Driver.IdentitySubject, audit.ActorSubject);
        Assert.Equal(accepted.StartsAt + AppHostFixture.BookingLimits.ArrivalAllowance, accepted.ArrivalDeadline);
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Book_InvalidOrOverlappingSlot_RejectsAdmission()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        await fixture.LoginAsync(page, "driver");
        var connector = await NorthConnectorAsync();
        string connectorId = connector.PublicId.ToString();
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var end = start.AddHours(1);
        Assert.Equal(201, (await PostAsync(page, connectorId, start, end)).GetProperty("status").GetInt32());
        Assert.Equal(409, (await PostAsync(page, connectorId, start, end)).GetProperty("status").GetInt32());
        Assert.Equal(400, (await PostAsync(page, connectorId, end, start)).GetProperty("status").GetInt32());
        Assert.Equal(
            400,
            (await PostAsync(page, connectorId, start, start.AddHours(4)))
                .GetProperty("status")
                .GetInt32());
        Assert.Equal(
            400,
            (await PostAsync(page, connectorId, start.AddDays(21), end.AddDays(21)))
                .GetProperty("status")
                .GetInt32());
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
            400,
            (await PostAsync(
                page,
                connectorId,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(5)))
                .GetProperty("status")
                .GetInt32());

        var otherConnectorId = await Storage.Connectors
            .Where(value => value.Evse!.Charger!.Station!.Name == "South Station")
            .Select(value => value.PublicId)
            .FirstAsync();
        var driverOverlap = await PostAsync(page, otherConnectorId.ToString(), start, end);
        Assert.Equal(409, driverOverlap.GetProperty("status").GetInt32());
        Assert.Contains(
            "already have a reservation",
            driverOverlap.GetProperty("body").GetProperty("detail").GetString());

    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Book_ConcurrentRequests_EnforcesConfiguredDriverQuota()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        await fixture.LoginAsync(page, "driver");
        string connectorId = (await NorthConnectorAsync()).PublicId.ToString();
        var end = DateTimeOffset.UtcNow.AddDays(10);
        var competitors = await page.EvaluateAsync<JsonElement>(
            """
            async bookings => {
                const session = await (await fetch('/auth/session')).json();
                return Promise.all(bookings.map(async booking => (await fetch('/api/reservations', {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.antiforgeryToken },
                    body: JSON.stringify(booking)
                })).status));
            }
            """,
            Enumerable.Range(0, 8)
                .Select(index => new
                    {
                        connectorId,
                        startsAt = end.AddHours(3 + index).ToString("O", CultureInfo.InvariantCulture),
                        endsAt = end.AddHours(4 + index).ToString("O", CultureInfo.InvariantCulture)
                    })
                .ToArray());
        var statuses = competitors.EnumerateArray().Select(status => status.GetInt32()).ToArray();
        Assert.Equal(
            AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver,
            statuses.Count(status => status == 201));
        Assert.Equal(
            8 - AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver,
            statuses.Count(status => status == 409));
        Assert.Equal(
            AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver,
            await Storage.Reservations.CountAsync(value =>
                value.DriverId == Driver.Id && value.State == ReservationState.Active));

    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task ListReservations_OwnedBookings_PagesNewestFirstAndRejectsForeignCursor()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        await fixture.LoginAsync(page, "driver");
        var storage = Storage;
        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var end = start.AddHours(1);
        var created = new List<Reservation>();
        for (var index = 0; index < 3; index++)
        {
            var booking = Reservation.Book(
                connector,
                Driver,
                start.AddHours(index),
                end.AddHours(index),
                DateTimeOffset.UtcNow,
                AppHostFixture.BookingLimits).Value;
            storage.Reservations.Add(booking);
            created.Add(booking);
        }

        await storage.SaveChangesAsync();
        var accepted = created[0];
        var foreignDriver = new Driver
        {
            IdentityIssuer = Driver.IdentityIssuer,
            IdentitySubject = Guid.NewGuid().ToString()
        };
        var foreignBooking = Reservation.Book(
            connector,
            foreignDriver,
            end.AddDays(2),
            end.AddDays(2).AddHours(1),
            DateTimeOffset.UtcNow,
            AppHostFixture.BookingLimits).Value;
        storage.Reservations.Add(foreignBooking);
        await storage.SaveChangesAsync();
        _additionalDrivers.Add(foreignDriver.Id);
        Guid foreignId = foreignBooking.PublicId;
        Assert.Equal(403, await StatusAsync(page, $"/api/reservations/{foreignId}"));
        Assert.Equal(400, await StatusAsync(page, $"/api/reservations?afterId={foreignId}"));
        var list = await GetAsync(page, "/api/reservations");
        Assert.DoesNotContain(
            list.GetProperty("reservations").EnumerateArray(),
            reservation => reservation.GetProperty("id").GetGuid() == foreignId);

        var reservations = await storage.Reservations.Where(value => value.DriverId == accepted.DriverId).ToListAsync();
        var latest = DateTimeOffset.UtcNow;
        foreach (var reservation in reservations)
        {
            storage.Entry(reservation).Property(value => value.CreatedAt).CurrentValue =
                reservation == accepted ? latest : latest.AddHours(-1);
        }

        await storage.SaveChangesAsync();
        var expected = reservations.OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Select(value => value.PublicId)
            .ToArray();

        var paged = new List<Guid>();
        string path = "/api/reservations?limit=2";
        do
        {
            var batch = await GetAsync(page, path);
            paged.AddRange(batch.GetProperty("reservations")
                .EnumerateArray()
                .Select(reservation => reservation.GetProperty("id").GetGuid()));
            Assert.True(paged.Count <= expected.Length, "Reservation paging repeated records.");
            path = batch.GetProperty("nextAfterId").ValueKind == JsonValueKind.Null
                ? ""
                : $"/api/reservations?limit=2&afterId={batch.GetProperty("nextAfterId").GetGuid()}";
        }
        while (path.Length > 0);

        Assert.Equal(expected, paged);

    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Book_OverdueAllocations_ReleasesCapacityExceptUnresolvedStarts()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = "Europe/London" });
        await fixture.LoginAsync(page, "driver");
        var storage = Storage;
        var driver = Driver;
        var connector = await NorthConnectorAsync();
        string connectorId = connector.PublicId.ToString();
        var past = DateTimeOffset.UtcNow.AddHours(-6);
        var overdue = Enumerable.Range(0, AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver)
            .Select(index => Reservation.Book(
                connector,
                driver,
                past.AddHours(index),
                past.AddHours(index + 1),
                past,
                AppHostFixture.BookingLimits).Value)
            .ToArray();
        storage.Reservations.AddRange(overdue);
        await storage.SaveChangesAsync();
        storage.Sessions.Add(new ChargingSession
        {
            ReservationId = overdue[0].Id,
            ConnectorId = connector.Id,
            Lifecycle = SessionLifecycle.Starting,
            CreatedAt = past
        });
        await storage.SaveChangesAsync();
        var upcoming = DateTimeOffset.UtcNow.AddHours(2);
        Assert.Equal(
            201,
            (await PostAsync(page, connectorId, upcoming, upcoming.AddHours(1)))
                .GetProperty("status")
                .GetInt32());
        Assert.Equal(
            3,
            await storage.Reservations.CountAsync(value =>
                value.DriverId == driver.Id && value.State == ReservationState.Expired));
        Assert.Equal(
            2,
            await storage.Reservations.CountAsync(value =>
                value.DriverId == driver.Id && value.State == ReservationState.Active));
    }

    private Task<EvCharging.Domain.Equipment.Connector> NorthConnectorAsync() => Storage.Connectors
        .Include(value => value.Evse!)
        .ThenInclude(value => value.Charger!)
        .ThenInclude(value => value.Station)
        .Where(value => value.Evse!.Charger!.Station!.Name == "North Station")
        .OrderBy(value => value.Number)
        .ThenBy(value => value.Id)
        .FirstAsync();

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Book_AnonymousOrOperator_DeniesDriverRoutes()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(fixture.App.GetEndpoint("frontend", "http").AbsoluteUri);
        Assert.Equal(401, await StatusAsync(page, "/api/reservations"));
        Assert.Equal(401, await StatusAsync(page, "/api/reservations/booking-policy"));
        await fixture.LoginAsync(page, "operator");
        Assert.Equal(403, await StatusAsync(page, "/api/reservations"));
        Assert.Equal(403, await StatusAsync(page, "/api/reservations/booking-policy"));
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
                startsAt = startsAt.ToString("O", CultureInfo.InvariantCulture),
                endsAt = endsAt.ToString("O", CultureInfo.InvariantCulture)
            });

    private static string DisplayTime(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture)
            .ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    private static async Task PickTimeAsync(IPage page, string label, string value)
    {
        var time = DateTime.Parse(value, CultureInfo.InvariantCulture);
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
                Name = time.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                Exact = true
            });
        if (await day.CountAsync() == 0)
        {
            await picker.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        }

        await day.ClickAsync();
        await picker.GetByRole(AriaRole.Spinbutton, new() { Name = $"{label} hours" })
            .FillAsync(time.ToString("HH", CultureInfo.InvariantCulture));
        await picker.GetByRole(AriaRole.Spinbutton, new() { Name = $"{label} minutes" })
            .FillAsync(time.ToString("mm", CultureInfo.InvariantCulture));
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

}
