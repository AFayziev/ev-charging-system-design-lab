using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed partial class WattHarborProviderTests
{
    private const int MaxDeliveryAttempts = 5;

    [Fact]
    public async Task Observations_ActiveSession_AreDeliveredWithIncreasingTimeAndEnergy()
    {
        var (request, start) = await StartSessionAsync();

        var received = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId, observations => observations.Count >= 4, "four observations");

        Assert.All(
            received,
            observation =>
            {
                Assert.Equal(CallbackReceiver.ApiKey, observation.ApiKey);
                Assert.Equal("charging", observation.State);
                Assert.Equal(22m, observation.PowerKw);
                Assert.False(observation.IsFinalEnergy);
            });
        Assert.Equal(received.Count, received.Select(observation => observation.EventId).Distinct().Count());
        Assert.Equal(start.AppliedAt, received[0].OccurredAt);
        Assert.Equal(0m, received[0].EnergyWh);
        Assert.All(
            received.Zip(received.Skip(1)),
            pair =>
            {
                Assert.True(pair.Second.OccurredAt > pair.First.OccurredAt);
                Assert.True(pair.Second.EnergyWh > pair.First.EnergyWh);
            });
    }

    [Fact]
    public async Task Observations_Stop_EndsWithOneFinalReadingAtTheEndOfTheSession()
    {
        var request = await ConfigureAsync();
        var startKey = Guid.NewGuid();
        var start = await StartAsync(startKey, request);
        await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId, observations => observations.Count >= 2, "two observations");
        var stopKey = Guid.NewGuid();
        var stop = await StopAsync(stopKey, new StopRequest(request.ExternalSessionId));
        // Replays and repeated stops must not record another end.
        await StopAsync(stopKey, new StopRequest(request.ExternalSessionId));
        await StopAsync(Guid.NewGuid(), new StopRequest(request.ExternalSessionId));
        await StartAsync(startKey, request);

        var received = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Any(observation => observation.State == "stopped"),
            "the final observation");
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        received = fixture.Receiver.For(request.ExternalSessionId);

        var final = Assert.Single(received, observation => observation.State == "stopped");
        Assert.True(final.IsFinalEnergy);
        Assert.Equal(stop.AppliedAt, final.OccurredAt);
        Assert.Equal(received.Max(observation => observation.OccurredAt), final.OccurredAt);
        Assert.Equal(received.Max(observation => observation.EnergyWh), final.EnergyWh);
        Assert.Equal(final.EventId, received[^1].EventId);
        Assert.Single(received, observation => observation.OccurredAt == start.AppliedAt);
    }

    [Fact]
    public async Task Observations_FailedDelivery_IsRetriedWithTheSameEventIdAndThenAbandoned()
    {
        var request = await ConfigureAsync();
        fixture.Receiver.RespondWith((int)HttpStatusCode.ServiceUnavailable);
        try
        {
            await StartAsync(Guid.NewGuid(), request);

            var received = await fixture.Receiver.WaitForAsync(
                request.ExternalSessionId,
                observations => observations.GroupBy(observation => observation.EventId)
                    .Any(attempts => attempts.Count() == MaxDeliveryAttempts),
                "the maximum number of delivery attempts");
            var firstEvent = received[0].EventId;
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            Assert.Equal(
                MaxDeliveryAttempts,
                fixture.Receiver.For(request.ExternalSessionId)
                    .Count(observation => observation.EventId == firstEvent));
        }
        finally
        {
            fixture.Receiver.RespondWith((int)HttpStatusCode.Accepted);
        }

        var lastFailure = fixture.Receiver.For(request.ExternalSessionId).Max(observation => observation.ReceivedAt);
        var recovered = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Any(observation => observation.ReceivedAt > lastFailure),
            "delivery to resume");
        Assert.NotEmpty(recovered);
    }

    [Fact]
    public async Task Observations_SeveralSessions_ShareCallbacks()
    {
        var (first, _) = await StartSessionAsync();
        var (second, _) = await StartSessionAsync();

        await fixture.Receiver.WaitForAsync(
            first.ExternalSessionId,
            observations => observations.Count >= 4
                && fixture.Receiver.For(second.ExternalSessionId).Count >= 4,
            "four observations of each session");

        var firstRequests = fixture.Receiver.For(first.ExternalSessionId).Select(observation => observation.RequestId);
        var secondRequests = fixture.Receiver.For(second.ExternalSessionId)
            .Select(observation => observation.RequestId);
        Assert.NotEmpty(firstRequests.Intersect(secondRequests));
        Assert.True(
            firstRequests.Concat(secondRequests).Distinct().Count() < firstRequests.Concat(secondRequests).Count());
    }

    [Fact]
    public async Task Snapshot_ReturnsTheLatestObservationWithTheSameIdentityAndTime()
    {
        var (request, _) = await StartSessionAsync();
        await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId, observations => observations.Count >= 2, "two observations");
        await StopAsync(Guid.NewGuid(), new StopRequest(request.ExternalSessionId));
        var received = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Any(observation => observation.State == "stopped"),
            "the final observation");
        var final = received.Single(observation => observation.State == "stopped");

        var snapshot = await fixture.Charging.GetFromJsonAsync<SnapshotResult>(
            $"/sessions/by-external-session/{request.ExternalSessionId}/snapshot");

        Assert.NotNull(snapshot);
        Assert.Equal(final.EventId, snapshot.EventId);
        Assert.Equal(final.OccurredAt, snapshot.OccurredAt);
        Assert.Equal(final.EnergyWh, snapshot.EnergyWh);
        Assert.Equal(request.ExternalSessionId, snapshot.ExternalSessionId);
        Assert.Equal(request.StationId, snapshot.StationId);
        Assert.Equal("stopped", snapshot.State);
        Assert.True(snapshot.IsFinalEnergy);
        using var missing = await fixture.Charging.GetAsync(
            $"/sessions/by-external-session/{Guid.NewGuid()}/snapshot");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Observations_DuplicateFault_SendsEachObservationTwiceWithTheSameEventId()
    {
        var request = await ConfigureAsync();
        await fixture.ConfigureTelemetryFaultAsync(request.ExternalSessionId, duplicate: true);
        await StartAsync(Guid.NewGuid(), request);

        var received = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Select(observation => observation.EventId).Distinct().Count() >= 3,
            "three distinct observations");

        var settled = received.GroupBy(observation => observation.EventId).Take(2);
        Assert.All(settled, duplicates => Assert.Equal(2, duplicates.Count()));
    }

    [Fact]
    public async Task Observations_OutOfOrderFault_DeliversANewerObservationBeforeAnOlderOne()
    {
        var request = await ConfigureAsync();
        await fixture.ConfigureTelemetryFaultAsync(request.ExternalSessionId, deliverOutOfOrder: true);
        await StartAsync(Guid.NewGuid(), request);

        var received = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Zip(observations.Skip(1))
                .Any(pair => pair.Second.OccurredAt < pair.First.OccurredAt),
            "an older observation to arrive after a newer one");

        Assert.Equal(received.Count, received.Select(observation => observation.EventId).Distinct().Count());
    }

    [Fact]
    public async Task Observations_InterruptFault_StopsDeliveryWhileTheSnapshotKeepsAdvancing()
    {
        var (request, _) = await StartSessionAsync();
        await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId, observations => observations.Count >= 2, "two observations");
        await fixture.ConfigureTelemetryFaultAsync(request.ExternalSessionId, interrupt: true);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var deliveredBefore = fixture.Receiver.For(request.ExternalSessionId).Count;
        var snapshotPath = $"/sessions/by-external-session/{request.ExternalSessionId}/snapshot";
        var before = await fixture.Charging.GetFromJsonAsync<SnapshotResult>(snapshotPath);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await StopAsync(Guid.NewGuid(), new StopRequest(request.ExternalSessionId));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var received = fixture.Receiver.For(request.ExternalSessionId);
        var after = await fixture.Charging.GetFromJsonAsync<SnapshotResult>(snapshotPath);
        Assert.Equal(deliveredBefore, received.Count);
        Assert.DoesNotContain(received, observation => observation.State == "stopped");
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after.OccurredAt > before.OccurredAt);
        Assert.Equal("stopped", after.State);
    }

    [Fact]
    public async Task Observations_ProviderRestart_ContinuesTheSameSessionWithRisingEnergy()
    {
        var (request, _) = await StartSessionAsync();
        var before = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId, observations => observations.Count >= 2, "two observations");
        var lastBefore = before.Max(observation => observation.OccurredAt);
        var energyBefore = before.Max(observation => observation.EnergyWh);

        await fixture.RestartProviderAsync();

        var after = await fixture.Receiver.WaitForAsync(
            request.ExternalSessionId,
            observations => observations.Count(observation => observation.OccurredAt > lastBefore) >= 2,
            "observations after the restart");
        Assert.All(
            after.Where(observation => observation.OccurredAt > lastBefore),
            observation => Assert.True(observation.EnergyWh > energyBefore));
    }

    private async Task<(StartRequest Request, CommandResult Start)> StartSessionAsync()
    {
        var request = await ConfigureAsync();
        return (request, await StartAsync(Guid.NewGuid(), request));
    }

    private sealed record SnapshotResult(
        Guid EventId,
        Guid ExternalSessionId,
        string StationId,
        string ChargerId,
        string ConnectorId,
        DateTimeOffset OccurredAt,
        string State,
        decimal PowerKw,
        decimal EnergyWh,
        bool IsFinalEnergy);
}
