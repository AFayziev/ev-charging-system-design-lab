using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RabbitMQ.Client;
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
        var browserTimeZone = Environment.GetEnvironmentVariable("TZ");
        ArgumentException.ThrowIfNullOrWhiteSpace(browserTimeZone, "TZ");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { TimezoneId = browserTimeZone });
        await fixture.LoginAsync(page, "driver");
        await page.GetByRole(AriaRole.Link, new() { Name = "View North Station", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Radio, new() { Name = "Connector", Exact = false }).First.ClickAsync();
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
        var bookingLimits = page.GetByRole(AriaRole.Region, new() { Name = "Booking limits" });
        await Expect(bookingLimits).ToContainTextAsync("Outstanding reservations: up to 4");
        await Expect(bookingLimits).ToContainTextAsync("Maximum slot duration: 3 hours");
        await Expect(bookingLimits).ToContainTextAsync("Book ahead: up to 20 days");
        var startsAt = DateTimeOffset.UtcNow.AddDays(10);
        var endsAt = startsAt.AddHours(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(browserTimeZone);
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
        await page.GetByRole(AriaRole.Radio, new() { Name = "Connector", Exact = false }).Last.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Connector" }))
            .ToHaveValueAsync("Connector 2 (Type 2, 22 kW)");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(startInput));
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "End time", Exact = true }))
            .ToHaveTextAsync(DisplayTime(endInput));
        await page.GetByRole(AriaRole.Radio, new() { Name = "Connector", Exact = false }).First.ClickAsync();
        await Expect(page.GetByText($"Your time · {browserTimeZone}", new() { Exact = true })).ToBeVisibleAsync();
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
        await Expect(page.GetByText($"Your time · {browserTimeZone}", new() { Exact = true })).ToBeVisibleAsync();
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        string endFormat = localStart.Date == localEnd.Date ? "HH:mm" : "d MMM yyyy HH:mm";
        string localSlot = localStart.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            + " - " + localEnd.ToString(endFormat, CultureInfo.InvariantCulture);
        await Expect(page.GetByText(localSlot, new() { Exact = true })).ToBeVisibleAsync();
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
        var page = await browser.NewPageAsync();
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
            .Where(value => value.Charger!.Station!.Name == "South Station")
            .Select(value => value.PublicId)
            .FirstAsync();
        var driverOverlap = await PostAsync(page, otherConnectorId.ToString(), start, end);
        Assert.Equal(409, driverOverlap.GetProperty("status").GetInt32());
        Assert.Contains(
            "already have a reservation",
            driverOverlap.GetProperty("body").GetProperty("detail").GetString());

    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Expire_WorkerRestart_UpdatesListAndReleasesQuota()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddSeconds(5);
        var response = await PostAsync(page, connector.PublicId.ToString(), start, start.AddSeconds(30));
        Assert.Equal(201, response.GetProperty("status").GetInt32());
        var publicId = response.GetProperty("body").GetProperty("id").GetGuid();

        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        var card = page.GetByRole(AriaRole.Article, new() { Name = "North Station reservation" });
        await Expect(card.GetByText("Active reservation", new() { Exact = true })).ToBeVisibleAsync();
        await WorkerCommandAsync(KnownResourceCommands.RestartCommand, timeout.Token);
        await Expect(card.GetByText("Expired", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 90_000 });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Active reservations", Exact = true }))
            .ToHaveCountAsync(0);
        var accepted = await Storage.Reservations.AsNoTracking()
            .SingleAsync(reservation => reservation.PublicId == publicId, timeout.Token);
        var audit = await Storage.AuditEntries.SingleAsync(
            entry => entry.ResourceId == accepted.Id && entry.Action == AuditAction.Updated,
            timeout.Token);
        Assert.Equal(ReservationState.Expired, accepted.State);
        Assert.NotNull(accepted.ExpiredAt);
        Assert.True(accepted.ExpiredAt >= accepted.EndsAt);
        await Expect(card.GetByText(new Regex("^Expired .+"))).ToBeVisibleAsync();
        var current = await GetAsync(page, $"/api/reservations/{publicId}");
        Assert.Equal(accepted.ExpiredAt, current.GetProperty("expiredAt").GetDateTimeOffset());
        Assert.Equal(AuditActorType.Service, audit.ActorType);
        Assert.Equal("charging-worker", audit.ActorSubject);
        Assert.False(string.IsNullOrWhiteSpace(audit.TraceId));

        var next = DateTimeOffset.UtcNow.AddHours(1);
        for (int index = 0; index < AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver; index++)
        {
            var booking = await PostAsync(
                page, connector.PublicId.ToString(), next.AddHours(index), next.AddHours(index + 1));
            Assert.Equal(201, booking.GetProperty("status").GetInt32());
        }
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Sweep_ExistingOverdueReservations_ExpiresEligibleAndRetainsUnresolvedStart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        await WorkerCommandAsync(KnownResourceCommands.StopCommand, timeout.Token);
        bool workerStopped = true;
        try
        {
            var connector = await NorthConnectorAsync();
            var past = DateTimeOffset.UtcNow.AddHours(-4);
            var bookings = Enumerable.Range(0, 3)
                .Select(index => Reservation.Book(
                    connector,
                    Driver,
                    past.AddHours(index),
                    past.AddHours(index + 1),
                    past,
                    AppHostFixture.BookingLimits).Value)
                .ToArray();
            Storage.Reservations.AddRange(bookings);
            await Storage.SaveChangesAsync(timeout.Token);
            Storage.Sessions.Add(new ChargingSession
            {
                ReservationId = bookings[0].Id,
                ConnectorId = connector.Id,
                Lifecycle = SessionLifecycle.Starting,
                CreatedAt = past
            });
            await Storage.SaveChangesAsync(timeout.Token);
            await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
            await Expect(page.GetByText("Active reservation", new() { Exact = true })).ToHaveCountAsync(3);

            await WorkerCommandAsync(KnownResourceCommands.StartCommand, timeout.Token);
            workerStopped = false;
            await Expect(page.GetByText("Expired", new() { Exact = true }))
                .ToHaveCountAsync(2, new() { Timeout = 60_000 });
            await Expect(page.GetByText("Active reservation", new() { Exact = true })).ToHaveCountAsync(1);
            var audits = await Storage.AuditEntries.Where(entry => entry.Action == AuditAction.Updated
                && bookings.Select(booking => booking.Id).Contains(entry.ResourceId))
                .ToListAsync(timeout.Token);
            Assert.Equal(2, audits.Count);
            Assert.All(audits, audit => Assert.Equal("charging-worker", audit.ActorSubject));
            Assert.DoesNotContain(audits, audit => audit.ResourceId == bookings[0].Id);
        }
        finally
        {
            if (workerStopped)
            {
                using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                await WorkerCommandAsync(KnownResourceCommands.StartCommand, restoreTimeout.Token);
            }
        }
    }

    private async Task WorkerCommandAsync(string command, CancellationToken cancellationToken)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync(
            "charging-worker", command, cancellationToken);
        Assert.True(result.Success, result.Message);
        string[] states = command == KnownResourceCommands.StopCommand
            ? [KnownResourceStates.Exited, KnownResourceStates.Finished] : [KnownResourceStates.Running];
        await fixture.App.ResourceNotifications.WaitForResourceAsync("charging-worker", states, cancellationToken);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Cancel_ActiveBooking_CommitsOnceAndReleasesQuota()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddDays(10);
        for (int index = 0; index < AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver; index++)
        {
            var booking = await PostAsync(
                page, connector.PublicId.ToString(), start.AddHours(index), start.AddHours(index + 1));
            Assert.Equal(201, booking.GetProperty("status").GetInt32());
        }

        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Active reservation", new() { Exact = true }))
            .ToHaveCountAsync(AppHostFixture.BookingLimits.MaximumOutstandingReservationsPerDriver);
        await CaptureAsync(page, "reservation-cancel-actions");
        var card = page.GetByRole(AriaRole.Article).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "Cancel reservation", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel reservation?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep reservation" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(card.GetByText("Active reservation", new() { Exact = true })).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(390, 844);
        await card.GetByRole(AriaRole.Button, new() { Name = "Cancel reservation", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm cancellation" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.GetByText("Cancelled", new() { Exact = true })).ToHaveCountAsync(1);
        var cancelled = await Storage.Reservations.AsNoTracking()
            .SingleAsync(value => value.DriverId == Driver.Id && value.State == ReservationState.Cancelled);
        Assert.NotNull(cancelled.CancelledAt);
        await Expect(page.GetByText(new Regex("^Cancelled .+"))).ToBeVisibleAsync();
        var repeats = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CancelAsync(page, cancelled.PublicId)));
        Assert.All(
            repeats,
            response =>
            {
                Assert.Equal(200, response.GetProperty("status").GetInt32());
                var returnedAt = response.GetProperty("body").GetProperty("cancelledAt").GetDateTimeOffset();
                Assert.Equal(cancelled.CancelledAt, returnedAt);
            });
        var audit = await Storage.AuditEntries.SingleAsync(entry => entry.ResourceId == cancelled.Id
            && entry.Action == AuditAction.Updated);
        Assert.Equal(Driver.IdentitySubject, audit.ActorSubject);
        Assert.Equal(Driver.IdentityIssuer, audit.ActorIssuer);
        Assert.Equal(
            (int)ReservationState.Cancelled, audit.Details.GetProperty("after").GetProperty("state").GetInt32());

        string? brokerConnection = await fixture.App.GetConnectionStringAsync("rabbitmq");
        Assert.NotNull(brokerConnection);
        var factory = new ConnectionFactory { Uri = new Uri(brokerConnection) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var message = await channel.BasicGetAsync(ReservationCancelled.Queue, autoAck: true);
        Assert.NotNull(message);
        using var body = JsonDocument.Parse(message.Body);
        Assert.Equal(cancelled.Id, body.RootElement.GetProperty("reservationId").GetGuid());
        var replacement = await PostAsync(
            page, connector.PublicId.ToString(), cancelled.StartsAt, cancelled.EndsAt);
        Assert.Equal(201, replacement.GetProperty("status").GetInt32());
        await page.ReloadAsync();
        await Expect(page.GetByText("Cancelled", new() { Exact = true })).ToHaveCountAsync(1);
        await Expect(page.GetByText(new Regex("^Cancelled .+"))).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("foreign", 403)]
    [InlineData("missing", 404)]
    [InlineData("expired", 409)]
    [InlineData("unresolved", 409)]
    [Trait("Category", "Api")]
    public async Task Cancel_IneligibleBooking_RejectsWithoutChanges(string scenario, int expectedStatus)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        if (scenario == "missing")
        {
            Assert.Equal(expectedStatus, (await CancelAsync(page, Guid.NewGuid())).GetProperty("status").GetInt32());
            return;
        }

        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var owner = Driver;
        if (scenario == "foreign")
        {
            owner = new Driver
            {
                IdentityIssuer = Driver.IdentityIssuer,
                IdentitySubject = Guid.NewGuid().ToString()
            };
        }

        var reservation = Reservation.Book(
            connector, owner, start, start.AddHours(1), start, AppHostFixture.BookingLimits).Value;
        if (scenario == "expired")
        {
            reservation.ExpireAtSlotEnd(start.AddHours(1), false);
        }

        Storage.Reservations.Add(reservation);
        await Storage.SaveChangesAsync();
        if (scenario == "foreign")
        {
            _additionalDrivers.Add(owner.Id);
        }

        if (scenario == "unresolved")
        {
            Storage.Sessions.Add(new ChargingSession
            {
                ReservationId = reservation.Id,
                ConnectorId = connector.Id,
                Lifecycle = SessionLifecycle.Starting,
                CreatedAt = start
            });
            await Storage.SaveChangesAsync();
        }

        var originalState = reservation.State;
        Assert.Equal(expectedStatus, (await CancelAsync(page, reservation.PublicId)).GetProperty("status").GetInt32());
        int auditCount = await Storage.AuditEntries.CountAsync(entry => entry.Action == AuditAction.Updated
            && entry.ResourceId == reservation.Id);
        Assert.Equal(0, auditCount);
        var saved = await Storage.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(originalState, saved.State);
    }

    private static Task<JsonElement> CancelAsync(IPage page, Guid id) => page.EvaluateAsync<JsonElement>(
        """
        async id => {
            const session = await (await fetch('/auth/session')).json();
            const response = await fetch(`/api/reservations/${id}/cancel`, {
                method: 'POST', headers: { 'X-CSRF-TOKEN': session.antiforgeryToken }
            });
            return { status: response.status, body: await response.json() };
        }
        """,
        id.ToString());

    [Fact]
    [Trait("Category", "Api")]
    public async Task Book_ConcurrentRequests_EnforcesConfiguredDriverQuota()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
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
        var page = await browser.NewPageAsync();
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
        var page = await browser.NewPageAsync();
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
        .Include(value => value.Charger!)
        .ThenInclude(value => value.Station)
        .Where(value => value.Charger!.Station!.Name == "North Station")
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
        int anonymousCancellation = await page.EvaluateAsync<int>(
            "async () => (await fetch('/api/reservations/00000000-0000-4000-8000-000000000000/cancel', { method: 'POST' })).status");
        Assert.Equal(401, anonymousCancellation);
        await fixture.LoginAsync(page, "operator");
        Assert.Equal(403, await StatusAsync(page, "/api/reservations"));
        Assert.Equal(403, await StatusAsync(page, "/api/reservations/booking-policy"));
        Assert.Equal(403, await StatusAsync(page, $"/api/reservations/{Guid.NewGuid()}"));
        var deniedCancellation = await CancelAsync(page, Guid.NewGuid());
        Assert.Equal(403, deniedCancellation.GetProperty("status").GetInt32());
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
