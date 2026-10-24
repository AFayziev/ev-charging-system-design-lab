using EvCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

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
}
