using System.Text.Json;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace EvCharging.E2E.Tests;

public sealed partial class ReservationTests
{
    /// <summary>What a limited vehicle takes: about eight seconds at the connector's 22 kW.</summary>
    private const decimal VehicleCapacityKWh = 0.05m;

    [Fact]
    [Trait("Category", "Api")]
    public async Task ProviderEnd_VehicleFull_CompletesTheSessionOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");

        var (reservationId, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token, VehicleCapacityKWh);

        var completed = await WaitForCompletedAsync(page, sessionId, timeout.Token);
        Assert.True(completed.GetProperty("finalEnergyKWh").GetDecimal() >= VehicleCapacityKWh);
        var session = await Storage.Sessions.AsNoTracking()
            .SingleAsync(value => value.PublicId == sessionId, timeout.Token);
        Assert.NotNull(session.CompletedAt);
        await AwaitCompletionNotificationAsync(session.ReservationId, timeout.Token);
        var late = await SubmitStopAsync(page, reservationId, Guid.NewGuid());
        Assert.Equal(409, late.GetProperty("status").GetInt32());
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task ProviderEnd_CallbacksInterrupted_CompletesFromTheProviderSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var (_, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token, VehicleCapacityKWh * 3);
        // The provider's end, including its terminal callback, never reaches the platform.
        await fixture.ConfigureWattHarborTelemetryFaultAsync(sessionId, interrupt: true);

        var completed = await WaitForCompletedAsync(page, sessionId, timeout.Token);

        Assert.True(completed.GetProperty("finalEnergyKWh").GetDecimal() >= VehicleCapacityKWh * 3);
    }

    private static Task<JsonElement> WaitForCompletedAsync(
        IPage page,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("lifecycle").GetString() == "completed",
            cancellationToken);

    private async Task AwaitCompletionNotificationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var completions = Storage.ReservationNotifications.AsNoTracking().Where(
            value => value.ReservationId == reservationId
                && value.Kind == ReservationNotificationKind.ChargingCompleted);
        while (!await completions.AnyAsync(cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        Assert.Equal(1, await completions.CountAsync(cancellationToken));
    }
}
