using System.Net;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

public sealed partial class ReservationTests
{
    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_LostResponse_AutomaticallyConfirmsWithoutRepeatingEffect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var operation = await StartWithLostResponseAsync(page, null, timeout.Token);

        await AwaitStartOutcomeAsync(operation.Id, CommandOutcome.Confirmed, timeout.Token);
        var attempt = await Storage.Attempts.AsNoTracking()
            .SingleAsync(value => value.OperationId == operation.Id, timeout.Token);
        Assert.Equal(CommandOutcome.Uncertain, attempt.Outcome);
        var confirmed = await Storage.Operations.AsNoTracking().Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id, timeout.Token);
        Assert.Equal(1, confirmed.AuthorizedAttemptCount);
        Assert.Equal(SessionLifecycle.Active, confirmed.Session!.Lifecycle);
        Assert.Equal(ReservationState.Consumed, confirmed.Session.Reservation!.State);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task CheckStart_ProviderRecoversAfterAutomaticWindow_ResolvesInTheBackgroundWithoutAnotherStart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var operation = await StartWithLostResponseAsync(page, HttpStatusCode.ServiceUnavailable, timeout.Token);
        await AwaitStartOutcomeAsync(operation.Id, CommandOutcome.Uncertain, timeout.Token);
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        var unresolved = await Storage.Operations.AsNoTracking().Include(value => value.Session!)
            .ThenInclude(value => value.Reservation)
            .SingleAsync(value => value.Id == operation.Id, timeout.Token);
        Assert.Equal(SessionLifecycle.Starting, unresolved.Session!.Lifecycle);
        Assert.Equal(ReservationState.Active, unresolved.Session.Reservation!.State);
        // Wait out the first checking window.
        var untilWindowEnds = unresolved.AutomaticChecksEndAt!.Value - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
        if (untilWindowEnds > TimeSpan.Zero)
        {
            await Task.Delay(untilWindowEnds, timeout.Token);
        }

        var stillUnresolved = await Storage.Operations.AsNoTracking()
            .SingleAsync(value => value.Id == operation.Id, timeout.Token);
        Assert.Equal(CommandOutcome.Uncertain, stillUnresolved.Outcome);

        await fixture.ConfigureWattHarborCommandFaultAsync(operation.PublicId, unresolved.Session.PublicId, null);

        // Settled by continued checks, without a manual status check.
        await Expect(page.GetByText("Charging started", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        Assert.Equal(1, await Storage.Attempts.CountAsync(value => value.OperationId == operation.Id, timeout.Token));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_CompetingKeys_AcceptsOneLogicalStart()
    {
        await PauseChargingAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await CurrentBookingAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid())));

        Assert.Equal(1, responses.Count(value => value.GetProperty("status").GetInt32() == 202));
        Assert.Equal(3, responses.Count(value => value.GetProperty("status").GetInt32() == 409));
        Assert.Equal(1, await Storage.Operations.CountAsync(value => value.DriverId == Driver.Id));
        Assert.Equal(1, await Storage.Sessions.CountAsync(value => value.ReservationId == reservation.Id));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_ExpiredAcceptedIntent_ReplaysBeforeCheckingNewEligibility()
    {
        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var reservation = Reservation.Book(
            connector,
            Driver,
            start,
            start.AddHours(1),
            start,
            AppHostFixture.BookingLimits).Value;
        Storage.Reservations.Add(reservation);
        await Storage.SaveChangesAsync();
        var key = Guid.NewGuid();
        var operation = Operation.AcceptStart(ChargingSession.AcceptStart(reservation, start).Value, key);
        Storage.Operations.Add(operation);
        await Storage.SaveChangesAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var replay = await SubmitStartAsync(page, reservation.PublicId, key);
        Assert.Equal(202, replay.GetProperty("status").GetInt32());
        Assert.Equal(operation.PublicId, replay.GetProperty("body").GetProperty("id").GetGuid());
        var newRequest = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(409, newRequest.GetProperty("status").GetInt32());
        Assert.Equal("start_window_ended", newRequest.GetProperty("body").GetProperty("code").GetString());
        Assert.Equal(1, await Storage.Sessions.CountAsync(value => value.ReservationId == reservation.Id));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_BrokerOutage_CommitsIntentAndDeliversStart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled: true);
        var stopped = await fixture.App.ResourceCommands.ExecuteCommandAsync(
            "rabbitmq", KnownResourceCommands.StopCommand, timeout.Token);
        Assert.True(stopped.Success, stopped.Message);
        await fixture.App.ResourceNotifications.WaitForResourceAsync(
            "rabbitmq", KnownResourceStates.Exited, timeout.Token);
        Guid operationId;
        try
        {
            var response = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
            Assert.Equal(202, response.GetProperty("status").GetInt32());
            var publicId = response.GetProperty("body").GetProperty("id").GetGuid();
            var operation = await Storage.Operations.SingleAsync(value => value.PublicId == publicId, timeout.Token);
            operationId = operation.Id;
            Assert.Equal(CommandOutcome.Pending, operation.Outcome);
            Assert.True(await Storage.Sessions.AnyAsync(value => value.Id == operation.SessionId, timeout.Token));
            Assert.True(await Storage.AuditEntries.AnyAsync(value => value.ResourceId == operation.Id, timeout.Token));
        }
        finally
        {
            var started = await fixture.App.ResourceCommands.ExecuteCommandAsync(
                "rabbitmq", KnownResourceCommands.StartCommand, CancellationToken.None);
            Assert.True(started.Success, started.Message);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync("rabbitmq", timeout.Token);
        }

        await AwaitStartOutcomeAsync(operationId, CommandOutcome.Confirmed, timeout.Token);
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_DuplicateRequests_PersistOneIntentAndRejectMismatchedPayload()
    {
        await PauseChargingAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await CurrentBookingAsync();
        var key = Guid.NewGuid();
        var submissions = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => SubmitStartAsync(page, reservation.PublicId, key)));
        Assert.All(submissions, value => Assert.Equal(202, value.GetProperty("status").GetInt32()));
        var publicId = submissions[0].GetProperty("body").GetProperty("id").GetGuid();
        Assert.All(submissions, value => Assert.Equal(publicId, value.GetProperty("body").GetProperty("id").GetGuid()));
        var operation = await Storage.Operations.Include(value => value.Session)
            .SingleAsync(value => value.PublicId == publicId);
        Assert.Equal(CommandOutcome.Pending, operation.Outcome);
        Assert.NotNull(operation.Session);
        Assert.NotEqual(operation.Id, publicId);
        Assert.NotEqual(operation.Session.Id, submissions[0].GetProperty("body").GetProperty("sessionId").GetGuid());
        Assert.Equal(1, await Storage.Sessions.CountAsync(value => value.ReservationId == reservation.Id));
        Assert.Equal(
            2,
            await Storage.AuditEntries.CountAsync(value => value.ResourceId == operation.Id
                || value.ResourceId == operation.Session.Id));
        var mismatch = await SubmitStartAsync(page, Guid.NewGuid(), key);
        Assert.Equal(409, mismatch.GetProperty("status").GetInt32());
        Assert.Equal("idempotency_mismatch", mismatch.GetProperty("body").GetProperty("code").GetString());

        var status = await GetAsync(page, $"/api/reservations/{reservation.PublicId}");
        Assert.Equal(publicId, status.GetProperty("startOperation").GetProperty("id").GetGuid());
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_CompetingCancellation_AllowsOnlyOneTransition()
    {
        await PauseChargingAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await CurrentBookingAsync();
        var start = SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        var cancellation = CancelAsync(page, reservation.PublicId);
        await Task.WhenAll(start, cancellation);
        int startStatus = (await start).GetProperty("status").GetInt32();
        int cancelStatus = (await cancellation).GetProperty("status").GetInt32();
        Assert.True((startStatus, cancelStatus) is (202, 409) or (409, 200));
        var saved = await Storage.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(startStatus == 202 ? ReservationState.Active : ReservationState.Cancelled, saved.State);
        Assert.Equal(
            startStatus == 202 ? 1 : 0,
            await Storage.Sessions.CountAsync(value => value.ReservationId == reservation.Id));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_ForeignReservation_DeniesStartAndStatusAccess()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var old = await PastForeignBookingAsync();
        var forbidden = await SubmitStartAsync(page, old.PublicId, Guid.NewGuid());
        Assert.Equal(403, forbidden.GetProperty("status").GetInt32());
        Assert.Equal(403, await StatusAsync(page, $"/api/reservations/{old.PublicId}"));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_OverrunConnectorClaim_RejectsWithoutCreatingIntent()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var old = await PastForeignBookingAsync();
        Storage.Sessions.Add(ChargingSession.AcceptStart(old, old.StartsAt).Value);
        await Storage.SaveChangesAsync();
        var current = await CurrentBookingAsync();
        var denied = await SubmitStartAsync(page, current.PublicId, Guid.NewGuid());
        Assert.Equal(409, denied.GetProperty("status").GetInt32());
        Assert.Equal("connector_busy", denied.GetProperty("body").GetProperty("code").GetString());
        Assert.False(await Storage.Sessions.AnyAsync(value => value.ReservationId == current.Id));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Start_BrowserConfirmation_RestoresPendingAfterReloadAndNavigation()
    {
        await PauseChargingAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var browserTimeZone = Environment.GetEnvironmentVariable("TZ");
        ArgumentException.ThrowIfNullOrWhiteSpace(browserTimeZone, "TZ");
        var page = await browser.NewPageAsync(new() { TimezoneId = browserTimeZone });
        await fixture.LoginAsync(page, "driver");
        await CurrentBookingAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true }).ClickAsync();
        await CaptureStartAsync(page, "confirmation");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start charging?" });
        await Expect(dialog.GetByText("Connect your vehicle to Connector 1 before starting.")).ToBeVisibleAsync();
        Assert.False(await Storage.Operations.AnyAsync(value => value.DriverId == Driver.Id));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Confirming start", new() { Exact = true })).ToBeVisibleAsync();
        await CaptureStartAsync(page, "pending");
        await page.ReloadAsync();
        await Expect(page.GetByText("Start pending", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Stations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Start pending", new() { Exact = true })).ToBeVisibleAsync();
        var startButton = page.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true });
        await Expect(startButton).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Cancel reservation" })).ToHaveCountAsync(0);
    }

    private async Task<Reservation> CurrentBookingAsync()
    {
        var connector = await NorthConnectorAsync();
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var reservation = Reservation.Book(
            connector, Driver, start, start.AddHours(1), start, AppHostFixture.BookingLimits).Value;
        Storage.Reservations.Add(reservation);
        await Storage.SaveChangesAsync();
        return reservation;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "Browser")]
    public async Task Start_ProviderOutcome_DisplaysConfirmedOrRejectedDriverResult(bool enabled)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start charging?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        var operation = await Storage.Operations.SingleAsync(value => value.Session!.ReservationId == reservation.Id);
        await AwaitStartOutcomeAsync(
            operation.Id,
            enabled ? CommandOutcome.Confirmed : CommandOutcome.Rejected,
            timeout.Token);
        await page.ReloadAsync();
        await Expect(page.GetByText(enabled ? "Charging started" : "Charging could not start", new() { Exact = true }))
            .ToBeVisibleAsync();
        if (enabled)
        {
            await Expect(page.GetByText("Reservation used", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start charging", Exact = true }))
                .ToHaveCountAsync(0);
        }
        else
        {
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Try starting again", Exact = true }))
                .ToBeVisibleAsync();
        }

        await CaptureStartAsync(page, enabled ? "confirmed" : "rejected");
    }

    private async Task<Operation> StartWithLostResponseAsync(
        IPage page, HttpStatusCode? lookupStatus, CancellationToken cancellationToken)
    {
        await PauseChargingAsync();
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled: true);
        await fixture.LoginAsync(page, "driver");
        var accepted = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var operationId = accepted.GetProperty("body").GetProperty("id").GetGuid();
        var sessionId = accepted.GetProperty("body").GetProperty("sessionId").GetGuid();
        await fixture.ConfigureWattHarborCommandFaultAsync(operationId, sessionId, (int?)lookupStatus);
        await WorkerCommandAsync(KnownResourceCommands.StartCommand, cancellationToken);
        _chargingPaused = false;
        return await Storage.Operations.SingleAsync(value => value.PublicId == operationId, cancellationToken);
    }

    private async Task PauseChargingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _chargingPaused = true;
        await WorkerCommandAsync(KnownResourceCommands.StopCommand, timeout.Token);
    }

    private async Task PrepareProviderEquipmentAsync(Reservation reservation, bool enabled)
    {
        var connector = await Storage.ProviderConnectors.Include(value => value.ProviderCharger!)
            .ThenInclude(value => value.ProviderStation)
            .SingleAsync(value => value.ConnectorId == reservation.ConnectorId);
        var charger = connector.ProviderCharger ?? throw new InvalidOperationException("Provider charger is required.");
        var station = charger.ProviderStation ?? throw new InvalidOperationException("Provider station is required.");
        connector.ExternalId = Guid.NewGuid().ToString();
        await Storage.SaveChangesAsync();
        await fixture.PrepareWattHarborConnectorAsync(
            station.ExternalId,
            charger.ExternalId,
            connector.ExternalId,
            enabled);
    }

    private async Task AwaitStartOutcomeAsync(Guid id, CommandOutcome outcome, CancellationToken cancellationToken)
    {
        while (!await Storage.Operations.AsNoTracking().AnyAsync(
            value => value.Id == id && value.Outcome == outcome,
            cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private async Task AwaitSessionLifecycleAsync(
        Guid sessionId, SessionLifecycle lifecycle, CancellationToken cancellationToken)
    {
        while (!await Storage.Sessions.AsNoTracking().AnyAsync(
            value => value.Id == sessionId && value.Lifecycle == lifecycle,
            cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static Task<JsonElement> SubmitStartAsync(IPage page, Guid reservationId, Guid key) =>
        PostKeyedAsync(page, $"/api/reservations/{reservationId}/start", key);

    private static Task<JsonElement> PostKeyedAsync(IPage page, string path, Guid key) =>
        page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const session = await (await fetch('/auth/session')).json();
                const response = await fetch(request.path, {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.antiforgeryToken },
                    body: JSON.stringify({ idempotencyKey: request.key })
                });
                return { status: response.status, body: await response.json().catch(() => null) };
            }
            """,
            new { path, key });

    private async Task<Reservation> PastForeignBookingAsync()
    {
        var connector = await NorthConnectorAsync();
        var foreign = Driver.Register(
            Driver.IdentityIssuer, Guid.NewGuid().ToString(), EmailAddress.Create("other@example.test").Value);
        Storage.Drivers.Add(foreign);
        await Storage.SaveChangesAsync();
        _additionalDrivers.Add(foreign.Id);
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        var reservation = Reservation.Book(
            connector, foreign, past, past.AddHours(1), past, AppHostFixture.BookingLimits).Value;
        Storage.Reservations.Add(reservation);
        await Storage.SaveChangesAsync();
        return reservation;
    }

    private static async Task CaptureStartAsync(IPage page, string state)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "start-screenshots");
        Directory.CreateDirectory(directory);
        foreach (var (name, width, height) in new[] { ("desktop", 1280, 900), ("mobile", 390, 844) })
        {
            await page.SetViewportSizeAsync(width, height);
            // Charts measure their container after a resize, so the layout settles a moment later.
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= window.innerWidth");
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(directory, $"{state}-{name}.png"),
                FullPage = true,
                Animations = ScreenshotAnimations.Disabled
            });
        }
    }
}
