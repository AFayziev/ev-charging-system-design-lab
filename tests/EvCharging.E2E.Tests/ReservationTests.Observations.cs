using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace EvCharging.E2E.Tests;

public sealed partial class ReservationTests
{
    [Fact]
    [Trait("Category", "Api")]
    public async Task Start_ConfirmedSession_StreamsProviderObservationsThroughTheGateway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled: true);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var accepted = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var operationId = accepted.GetProperty("body").GetProperty("id").GetGuid();
        var sessionId = accepted.GetProperty("body").GetProperty("sessionId").GetGuid();
        var operation = await Storage.Operations.AsNoTracking()
            .SingleAsync(value => value.PublicId == operationId, timeout.Token);
        await AwaitStartOutcomeAsync(operation.Id, Domain.Charging.CommandOutcome.Confirmed, timeout.Token);

        var envelopes = await fixture.ReadObservationsAsync(
            sessionId, observations => observations.Count >= 3, timeout.Token);

        Assert.All(
            envelopes,
            envelope =>
            {
                Assert.Equal(1, envelope.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("driver-wattharbor", envelope.GetProperty("integrationKey").GetString());
                Assert.Equal("charging", envelope.GetProperty("state").GetString());
            });
        var energies = envelopes.Select(envelope => envelope.GetProperty("energyKWh").GetDecimal()).Order().ToArray();
        Assert.Equal(0m, energies[0]);
        Assert.True(energies[^1] > energies[0]);
    }
}
