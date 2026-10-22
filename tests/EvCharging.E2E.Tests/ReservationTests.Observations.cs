using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
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
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var (_, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token);

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

    [Fact]
    [Trait("Category", "Api")]
    public async Task Progress_ObservationWorkerRestart_CatchesUpFromTheStream()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var (_, sessionId) = await StartConfirmedSessionAsync(page, timeout.Token);
        var running = await WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("reading").ValueKind == JsonValueKind.Object,
            timeout
                .Token);
        Assert.Equal(sessionId, running.GetProperty("sessionId").GetGuid());

        await WorkerCommandAsync(KnownResourceCommands.StopCommand, timeout.Token, "observation-worker");
        var stoppedAt = DateTimeOffset.UtcNow;
        var produced = await fixture.ReadObservationsAsync(
            sessionId,
            observations => observations.Any(
                envelope => envelope.GetProperty("occurredAt").GetDateTimeOffset() > stoppedAt.AddSeconds(2)),
            timeout.Token);
        var newest = produced.Max(envelope => envelope.GetProperty("occurredAt").GetDateTimeOffset());
        var stale = await WaitForProgressAsync(
            page, sessionId, progress => progress.GetProperty("isStale").GetBoolean(), timeout.Token);
        Assert.True(stale.GetProperty("reading").GetProperty("occurredAt").GetDateTimeOffset() < newest);

        await WorkerCommandAsync(KnownResourceCommands.StartCommand, timeout.Token, "observation-worker");

        var caughtUp = await WaitForProgressAsync(
            page,
            sessionId,
            progress => progress.GetProperty("reading").GetProperty("occurredAt").GetDateTimeOffset() >= newest,
            timeout.Token);
        Assert.Equal("charging", caughtUp.GetProperty("reading").GetProperty("state").GetString());
    }

    private async Task<(Guid ReservationId, Guid SessionId)> StartConfirmedSessionAsync(
        IPage page, CancellationToken cancellationToken)
    {
        var reservation = await CurrentBookingAsync();
        await PrepareProviderEquipmentAsync(reservation, enabled: true);
        var accepted = await SubmitStartAsync(page, reservation.PublicId, Guid.NewGuid());
        Assert.Equal(202, accepted.GetProperty("status").GetInt32());
        var operationId = accepted.GetProperty("body").GetProperty("id").GetGuid();
        var operation = await Storage.Operations.AsNoTracking()
            .SingleAsync(value => value.PublicId == operationId, cancellationToken);
        await AwaitStartOutcomeAsync(operation.Id, Domain.Charging.CommandOutcome.Confirmed, cancellationToken);
        return (reservation.PublicId, accepted.GetProperty("body").GetProperty("sessionId").GetGuid());
    }

    private static async Task<JsonElement> WaitForProgressAsync(
        IPage page, Guid sessionId, Func<JsonElement, bool> condition, CancellationToken cancellationToken)
    {
        var last = default(JsonElement);
        while (true)
        {
            last = await page.EvaluateAsync<JsonElement>(
                """
                async path => {
                    const response = await fetch(path);
                    return { status: response.status, body: await response.json().catch(() => null) };
                }
                """,
                $"/api/charging-sessions/{sessionId}/progress");
            if (last.GetProperty("status").GetInt32() == 200 && condition(last.GetProperty("body")))
            {
                return last.GetProperty("body");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"The progress condition was not met; the last response was {last}.");
            }
        }
    }
}
