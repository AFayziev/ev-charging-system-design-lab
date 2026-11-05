using System.Net;
using EvCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

public sealed partial class ReservationTests
{
    [Fact]
    [Trait("Category", "Api")]
    public async Task Progress_CallbacksInterrupted_StaleSessionIsReadFromTheProvider()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var (_, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token);
        await WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("reading").ValueKind == System.Text.Json.JsonValueKind.Object,
            timeout.Token);
        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: true);
        var interruptedAt = DateTimeOffset.UtcNow;

        // No callback arrives, so a reading newer than the interruption can only come from the provider's snapshot.
        var recovered = await WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("reading").GetProperty("occurredAt").GetDateTimeOffset()
                > interruptedAt.AddSeconds(5),
            timeout.Token);

        Assert.Equal("charging", recovered.GetProperty("reading").GetProperty("state").GetString());
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Stop_CallbacksInterrupted_FinalReadingArrivesThroughTheProviderSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var (reservationId, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token);
        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: true);

        var accepted = await SubmitStopAsync(page, reservationId, Guid.NewGuid());

        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var session = await Storage.Sessions.AsNoTracking()
            .SingleAsync(value => value.PublicId == sessionId, timeout.Token);
        await AwaitSessionLifecycleAsync(session.Id, SessionLifecycle.Completed, timeout.Token);
        var completed = await Storage.Sessions.AsNoTracking()
            .SingleAsync(value => value.Id == session.Id, timeout.Token);
        Assert.NotNull(completed.CompletedAt);
        Assert.NotNull(completed.FinalEnergyKWh);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Release_UncertainStartWithNoObservations_OperatorFreesTheConnectorAndTheDriverSeesIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var driverPage = await browser.NewPageAsync();
        var operation = await StartWithLostResponseAsync(
            driverPage, HttpStatusCode.ServiceUnavailable, timeout.Token, interruptTelemetry: true);
        await AwaitStartOutcomeAsync(operation.Id, CommandOutcome.Uncertain, timeout.Token);
        var lastRequestedAt = await Storage.Attempts.AsNoTracking()
            .Where(value => value.OperationId == operation.Id)
            .MaxAsync(value => value.AuthorizedAt, timeout.Token);
        await using var operatorContext = await browser.NewContextAsync();
        var operatorPage = await operatorContext.NewPageAsync();
        await fixture.LoginAsync(operatorPage, "operator");
        await operatorPage.GetByRole(AriaRole.Link, new() { Name = "Operator area" }).ClickAsync();
        await operatorPage.GetByRole(AriaRole.Link, new() { Name = "Unsettled charging operations" }).ClickAsync();
        var card = operatorPage.GetByRole(AriaRole.Region).Filter(new() { HasText = operation.PublicId.ToString() });
        await Expect(card.GetByText("The connector can be released from", new() { Exact = false })).ToBeVisibleAsync();

        // The release window that the fixture shortens has to pass; the server decides when it has.
        var releaseButton = card.GetByRole(AriaRole.Button, new() { NameRegex = new("^Release connector") });
        var untilReleasable = lastRequestedAt + AppHostFixture.ReleaseWindow - DateTimeOffset.UtcNow;
        await Task.Delay(untilReleasable > TimeSpan.Zero ? untilReleasable : TimeSpan.Zero, timeout.Token);
        while (!await releaseButton.IsVisibleAsync())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await operatorPage.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();
            await Expect(operatorPage.GetByRole(AriaRole.Button, new() { Name = "Refresh" })).ToBeEnabledAsync();
        }

        await releaseButton.ClickAsync();
        var dialog = operatorPage.GetByRole(AriaRole.Dialog, new() { Name = "Release connector?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Release connector" }).ClickAsync();
        await Expect(card).ToHaveCountAsync(0);

        var released = await Storage.Operations.AsNoTracking().Include(value => value.Session)
            .SingleAsync(value => value.Id == operation.Id, timeout.Token);
        Assert.Equal(CommandOutcome.Uncertain, released.Outcome);
        Assert.NotNull(released.ReleasedAt);
        Assert.Equal(SessionLifecycle.Failed, released.Session!.Lifecycle);
        await driverPage.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await Expect(driverPage.GetByText("Charging start released", new() { Exact = true })).ToBeVisibleAsync();
    }
}
