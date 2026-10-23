using System.Text.Json;
using EvCharging.Domain.Charging;
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
    public async Task Stop_ActiveSession_AcceptsOneLogicalStopAndReplaysDuplicates()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await ActiveSessionBookingAsync();
        var key = Guid.NewGuid();

        var submissions = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => SubmitStopAsync(page, reservation.PublicId, key)));

        Assert.All(submissions, value => Assert.Equal(202, value.GetProperty("status").GetInt32()));
        var publicId = submissions[0].GetProperty("body").GetProperty("id").GetGuid();
        Assert.All(submissions, value => Assert.Equal(publicId, value.GetProperty("body").GetProperty("id").GetGuid()));
        var operation = await Storage.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.PublicId == publicId);
        Assert.Equal(ChargingCommandKind.Stop, operation.Kind);
        Assert.Equal(CommandOutcome.Pending, operation.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, operation.Session!.Lifecycle);
        Assert.Equal(
            1,
            await Storage.Operations.CountAsync(
                value => value.Session!.ReservationId == reservation.Id && value.Kind == ChargingCommandKind.Stop));
        Assert.True(await Storage.AuditEntries.AnyAsync(value => value.ResourceId == operation.Id));
        var mismatch = await SubmitStopAsync(page, Guid.NewGuid(), key);
        Assert.Equal(409, mismatch.GetProperty("status").GetInt32());
        Assert.Equal("idempotency_mismatch", mismatch.GetProperty("body").GetProperty("code").GetString());

        var status = await GetAsync(page, $"/api/reservations/{reservation.PublicId}");
        Assert.Equal(publicId, status.GetProperty("stopOperation").GetProperty("id").GetGuid());
        Assert.Equal("stopping", status.GetProperty("startOperation").GetProperty("lifecycle").GetString());
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Stop_CompetingKeys_AcceptsOneLogicalStop()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var reservation = await ActiveSessionBookingAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => SubmitStopAsync(page, reservation.PublicId, Guid.NewGuid())));

        Assert.Equal(1, responses.Count(value => value.GetProperty("status").GetInt32() == 202));
        var conflicts = responses.Where(value => value.GetProperty("status").GetInt32() == 409).ToArray();
        Assert.Equal(3, conflicts.Length);
        Assert.All(
            conflicts,
            value => Assert.Equal("stop_in_progress", value.GetProperty("body").GetProperty("code").GetString()));
        Assert.Equal(
            1,
            await Storage.Operations.CountAsync(
                value => value.Session!.ReservationId == reservation.Id && value.Kind == ChargingCommandKind.Stop));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Stop_SessionNotActive_ReturnsConflictWithoutCreatingIntent()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var unused = await CurrentBookingAsync();
        var noSession = await SubmitStopAsync(page, unused.PublicId, Guid.NewGuid());
        Assert.Equal(409, noSession.GetProperty("status").GetInt32());
        Assert.Equal("session_not_active", noSession.GetProperty("body").GetProperty("code").GetString());

        var pending = ChargingSession.AcceptStart(unused, unused.StartsAt).Value;
        Storage.Operations.Add(Operation.AcceptStart(pending, Guid.NewGuid()));
        await Storage.SaveChangesAsync();
        var starting = await SubmitStopAsync(page, unused.PublicId, Guid.NewGuid());
        Assert.Equal(409, starting.GetProperty("status").GetInt32());
        Assert.Equal("session_not_active", starting.GetProperty("body").GetProperty("code").GetString());
        Assert.False(await Storage.Operations.AnyAsync(
            value => value.Session!.ReservationId == unused.Id && value.Kind == ChargingCommandKind.Stop));
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Stop_ForeignOrUnknownReservation_IsDenied()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var foreign = await PastForeignBookingAsync();

        var forbidden = await SubmitStopAsync(page, foreign.PublicId, Guid.NewGuid());
        var missing = await SubmitStopAsync(page, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(403, forbidden.GetProperty("status").GetInt32());
        Assert.Equal(404, missing.GetProperty("status").GetInt32());
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Stop_LostResponse_ConfirmsWithoutRepeatingEffectAndCompletesFromTheFinalReading()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var reservation = await StartConfirmedAsync(page, timeout.Token);
        var sessionId = await Storage.Sessions.AsNoTracking().Where(value => value.ReservationId == reservation.Id)
            .Select(value => value.PublicId)
            .SingleAsync(timeout.Token);
        await fixture.ConfigureWattHarborCommandFaultAsync(Guid.NewGuid(), sessionId, null);
        var accepted = await SubmitStopAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var publicId = accepted.GetProperty("body").GetProperty("id").GetGuid();

        var stop = await Storage.Operations.AsNoTracking().SingleAsync(value => value.PublicId == publicId);
        await AwaitStartOutcomeAsync(stop.Id, CommandOutcome.Confirmed, timeout.Token);

        var attempt = await Storage.Attempts.AsNoTracking().SingleAsync(value => value.OperationId == stop.Id);
        Assert.Equal(CommandOutcome.Uncertain, attempt.Outcome);
        var confirmed = await Storage.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == stop.Id, timeout.Token);
        Assert.Equal(1, confirmed.AuthorizedAttemptCount);
        // The provider ended the session, so its final reading completes the session whatever the stop's outcome.
        await AwaitSessionLifecycleAsync(confirmed.SessionId, SessionLifecycle.Completed, timeout.Token);
        var completed = await Storage.Sessions.AsNoTracking()
            .SingleAsync(value => value.Id == confirmed.SessionId, timeout.Token);
        Assert.NotNull(completed.CompletedAt);
        Assert.NotNull(completed.FinalEnergyKWh);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Stop_BrowserConfirmation_ShowsStoppedWithFinalUsagePending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var reservation = await StartConfirmedAsync(page, timeout.Token);
        var sessionId = await Storage.Sessions.AsNoTracking().Where(value => value.ReservationId == reservation.Id)
            .Select(value => value.PublicId)
            .SingleAsync(timeout.Token);
        // Without the provider's final reading there is no final usage, so the session cannot complete.
        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: true);
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop charging", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Stop charging?" });
        Assert.False(await Storage.Operations.AnyAsync(
            value => value.Kind == ChargingCommandKind.Stop && value.DriverId == Driver.Id, timeout.Token));

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Stop charging", Exact = true }).ClickAsync();

        await Expect(page.GetByText("Charging stopped", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText("Stopped · final usage pending", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Stop charging", Exact = true }))
            .ToHaveCountAsync(0);
        var confirmed = await Storage.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Kind == ChargingCommandKind.Stop && value.DriverId == Driver.Id, timeout.Token);
        Assert.Equal(CommandOutcome.Confirmed, confirmed.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, confirmed.Session!.Lifecycle);
        var attempt = await Storage.Attempts.AsNoTracking().SingleAsync(value => value.OperationId == confirmed.Id);
        Assert.Equal(CommandOutcome.Confirmed, attempt.Outcome);
        Assert.NotNull(attempt.ProviderCommandId);
    }

    [Theory]
    [InlineData(SessionLifecycle.Active)]
    [InlineData(SessionLifecycle.Stopping)]
    [Trait("Category", "Api")]
    public async Task Start_AfterSlotOverrun_StaysBlockedUntilTheSessionEnds(SessionLifecycle lifecycle)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var overrun = await PastForeignBookingAsync();
        var provider = await Storage.ChargingProviders.FirstAsync();
        var session = ChargingSession.AcceptStart(overrun, overrun.StartsAt).Value;
        var start = Operation.AcceptStart(session, Guid.NewGuid());
        Storage.Operations.Add(start);
        Assert.True(start.ConfirmStart(provider.Id, Guid.NewGuid().ToString(), overrun.StartsAt).IsSuccess);
        if (lifecycle == SessionLifecycle.Stopping)
        {
            var stop = Operation.AcceptStop(session, Guid.NewGuid(), overrun.EndsAt.AddMinutes(5)).Value;
            Assert.True(stop.ConfirmStop().IsSuccess);
            Storage.Operations.Add(stop);
        }

        await Storage.SaveChangesAsync();
        var current = await CurrentBookingAsync();

        var denied = await SubmitStartAsync(page, current.PublicId, Guid.NewGuid());

        Assert.Equal(409, denied.GetProperty("status").GetInt32());
        Assert.Equal("connector_busy", denied.GetProperty("body").GetProperty("code").GetString());
        Assert.False(await Storage.Sessions.AnyAsync(value => value.ReservationId == current.Id));
    }

    /// <summary>Books the current slot and runs a real start through the provider until it is confirmed.</summary>
    private async Task<Reservation> StartConfirmedAsync(IPage page, CancellationToken cancellationToken)
    {
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled: true);
        await fixture.LoginAsync(page, "driver");
        var accepted = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var publicId = accepted.GetProperty("body").GetProperty("id").GetGuid();
        var start = await Storage.Operations.AsNoTracking()
            .SingleAsync(value => value.PublicId == publicId, cancellationToken);
        await AwaitStartOutcomeAsync(start.Id, CommandOutcome.Confirmed, cancellationToken);
        return reservation;
    }

    /// <summary>
    /// Books the current slot and confirms its start, leaving an active session. It has no recorded provider
    /// request, so the worker cannot execute its stop and leaves the accepted intent untouched.
    /// </summary>
    private async Task<Reservation> ActiveSessionBookingAsync()
    {
        var reservation = await CurrentBookingAsync();
        var provider = await Storage.ChargingProviders.FirstAsync();
        var session = ChargingSession.AcceptStart(reservation, reservation.StartsAt).Value;
        var start = Operation.AcceptStart(session, Guid.NewGuid());
        Storage.Operations.Add(start);
        Assert.True(start.ConfirmStart(provider.Id, Guid.NewGuid().ToString(), reservation.StartsAt).IsSuccess);
        await Storage.SaveChangesAsync();
        return reservation;
    }

    private static Task<JsonElement> SubmitStopAsync(IPage page, Guid reservationId, Guid key) =>
        PostKeyedAsync(page, $"/api/reservations/{reservationId}/stop", key);
}
