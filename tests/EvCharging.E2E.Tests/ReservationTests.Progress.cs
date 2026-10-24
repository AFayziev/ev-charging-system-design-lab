using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using EvCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

public sealed partial class ReservationTests
{
    [Fact]
    [Trait("Category", "Browser")]
    public async Task Progress_BrowserSession_ShowsLiveFreshAndStaleProgress()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Url);
        int ProgressRequests() => requests.Count(
            url => url.EndsWith("/progress", StringComparison.Ordinal));
        await fixture.LoginAsync(page, "driver");
        var (_, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token);

        // The chart asks only for what it does not hold yet.
        var incrementalPower = page.WaitForRequestAsync(
            request => request.Url.Contains($"/charging-sessions/{sessionId}/power?from="),
            new() { Timeout = 90_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View charging", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Charging session" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Charging", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByText(new Regex(@"^\d+\.\d\d kWh$"))).ToBeVisibleAsync();
        await Expect(page.Locator("svg.recharts-surface").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await CaptureStartAsync(page, "charging-live");

        await incrementalPower;

        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: true, snapshotUnavailable: true);
        await Expect(page.GetByText("Last reported charging", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(page.GetByText(new Regex(@"Last update \d\d:\d\d:\d\d"))).ToBeVisibleAsync();
        await CaptureStartAsync(page, "charging-stale");

        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: false);
        await Expect(page.GetByText("Charging", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await page.GetByRole(AriaRole.Link, new() { Name = "My reservations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop charging", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Stop charging?" })
            .GetByRole(AriaRole.Button, new() { Name = "Stop charging", Exact = true })
            .ClickAsync();
        // The stop is accepted by the click's request; leaving the page earlier could abandon it.
        Operation? stop;
        while ((stop = await Storage.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Kind == ChargingCommandKind.Stop && value.DriverId == Driver.Id, timeout.Token)) is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        await AwaitStartOutcomeAsync(stop.Id, CommandOutcome.Confirmed, timeout.Token);
        // The provider's final reading completes the session, which may already have happened.
        await page.GotoAsync(new Uri(
            fixture.App.GetEndpoint("frontend", "http"), $"/charging-sessions/{sessionId}").AbsoluteUri);
        await Expect(page.GetByText("Charging completed", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(page.GetByText(new Regex(@"^\d+\.\d\d kWh$"))).ToBeVisibleAsync();
        await CaptureStartAsync(page, "charging-completed");

        // Completion ends the polling.
        await WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("lifecycle").GetString() == "completed",
            timeout.Token);
        await Task.Delay(TimeSpan.FromSeconds(6), timeout.Token);
        var polled = ProgressRequests();
        await Task.Delay(TimeSpan.FromSeconds(12), timeout.Token);
        Assert.Equal(polled, ProgressRequests());
    }
}
