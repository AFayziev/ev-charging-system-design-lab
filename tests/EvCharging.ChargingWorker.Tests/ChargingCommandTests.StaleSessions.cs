using System.Net;
using System.Net.Http.Json;
using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Observations;
using EvCharging.Domain.Observations;
using EvCharging.ObservationStream;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed partial class ChargingCommandTests
{
    private static readonly TimeSpan FreshnessLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task FindStale_ActiveSessionPastTheFreshnessLimit_RequestsOneRead()
    {
        var sessionId = await ActiveSessionAsync();
        AdvancePastFreshnessLimit();

        var scans = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => FindStaleAsync(sessionId)));

        var refresh = Assert.Single(scans.SelectMany(messages => messages));
        Assert.Equal(sessionId, refresh.SessionId);
        await using var database = Database();
        var saved = await database.Sessions.AsNoTracking().SingleAsync(value => value.Id == sessionId);
        Assert.True(saved.NextEvidenceReadAt > _clock.GetUtcNow());
        Assert.Empty(await FindStaleAsync(sessionId));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Single(await FindStaleAsync(sessionId));
    }

    [Fact]
    public async Task FindStale_RecentReading_IsLeftAlone()
    {
        var sessionId = await ActiveSessionAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        await ReportAsync(sessionId);

        Assert.Empty(await FindStaleAsync(sessionId));
        AdvancePastFreshnessLimit();
        Assert.Single(await FindStaleAsync(sessionId));
    }

    [Fact]
    public async Task FindStale_CompletedSession_IsLeftAlone()
    {
        var sessionId = await ActiveSessionAsync();
        await CompleteSessionAsync(sessionId);
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(await FindStaleAsync(sessionId));
    }

    [Fact]
    public async Task FindStale_LongStale_ReadsLessOftenUpToTheLimit()
    {
        var sessionId = await ActiveSessionAsync();
        _clock.Advance(TimeSpan.FromHours(8));

        Assert.Single(await FindStaleAsync(sessionId));

        await using var database = Database();
        var saved = await database.Sessions.AsNoTracking().SingleAsync(value => value.Id == sessionId);
        var gap = saved.NextEvidenceReadAt!.Value - _clock.GetUtcNow();
        Assert.InRange(gap, TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Refresh_ProviderHasAnObservation_AppendsItToTheStreamWithItsOwnIdentity()
    {
        var sessionId = await ActiveSessionAsync();
        var sessionPublicId = await SessionPublicIdAsync(sessionId);
        var eventId = Guid.NewGuid();
        var occurredAt = _clock.GetUtcNow().AddSeconds(-3);
        using var provider = SnapshotTransport(
            sessionPublicId, eventId, occurredAt, "stopped", energyWh: 12500m, isFinalEnergy: true);
        AdvancePastFreshnessLimit();
        var stream = new RecordingStream();

        await RefreshAsync(sessionId, provider, stream);

        var envelope = Assert.Single(stream.Appended);
        Assert.Equal(IntegrationKey, envelope.IntegrationKey);
        Assert.Equal(eventId, envelope.SourceEventId);
        Assert.Equal(sessionPublicId, envelope.ExternalSessionId);
        Assert.Equal(occurredAt.ToUniversalTime(), envelope.OccurredAt);
        Assert.Equal(_clock.GetUtcNow(), envelope.ReceivedAt);
        Assert.Equal(ObservationState.Stopped, envelope.State);
        Assert.Equal(12.5m, envelope.EnergyKWh);
        Assert.True(envelope.IsFinalEnergy);
        Assert.NotEqual(eventId, envelope.ObservationId);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Refresh_ProviderHasNoUsableObservation_AppendsNothing(HttpStatusCode status)
    {
        var sessionId = await ActiveSessionAsync();
        using var provider = new ProviderTransport((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        AdvancePastFreshnessLimit();
        var stream = new RecordingStream();

        await RefreshAsync(sessionId, provider, stream);

        Assert.Empty(stream.Appended);
    }

    [Fact]
    public async Task Refresh_StreamDoesNotConfirm_LeavesTheRetryToTheNextRead()
    {
        var sessionId = await ActiveSessionAsync();
        using var provider = SnapshotTransport(
            await SessionPublicIdAsync(sessionId), Guid.NewGuid(), _clock.GetUtcNow(), "charging", energyWh: 100m);
        AdvancePastFreshnessLimit();
        var stream = new RecordingStream
        {
            Failure = new ObservationStreamException("down", new InvalidOperationException())
        };

        await RefreshAsync(sessionId, provider, stream);

        // The failure is absorbed, not rethrown into the queue.
        Assert.Equal(1, stream.Attempts);
    }

    [Fact]
    public async Task Refresh_SessionReportedSinceTheScan_DoesNotCallTheProvider()
    {
        var sessionId = await ActiveSessionAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        await ReportAsync(sessionId);
        using var provider = new ProviderTransport((_, _) => throw new InvalidOperationException("No call expected."));

        await RefreshAsync(sessionId, provider, new RecordingStream());

        Assert.Equal(0, provider.Calls);
    }

    private void AdvancePastFreshnessLimit() => _clock.Advance(FreshnessLimit + TimeSpan.FromSeconds(1));

    // The provider's newest observation of the session.
    private static ProviderTransport SnapshotTransport(
        Guid sessionPublicId,
        Guid eventId,
        DateTimeOffset occurredAt,
        string state,
        decimal energyWh,
        bool isFinalEnergy = false) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                new
                {
                    eventId,
                    externalSessionId = sessionPublicId,
                    stationId = "station",
                    chargerId = "charger",
                    connectorId = "connector",
                    occurredAt,
                    state,
                    powerKw = 0m,
                    energyWh,
                    isFinalEnergy
                })
        }));

    private async Task ReportAsync(Guid sessionId)
    {
        await using var database = Database();
        var evidence = SessionEvidence.Create(sessionId);
        evidence.Record(_clock.GetUtcNow(), _clock.GetUtcNow(), 1m, false, TimeSpan.FromSeconds(5));
        database.SessionEvidence.Add(evidence);
        await database.SaveChangesAsync();
    }

    private async Task<Guid> ActiveSessionAsync()
    {
        var operation = await PrepareAsync();
        using var applied = new ProviderTransport((_, _) => Task.FromResult(Applied(operation.PublicId)));
        await ExecuteAsync(operation.Id, applied);
        return operation.SessionId;
    }

    private async Task<Guid> SessionPublicIdAsync(Guid sessionId)
    {
        await using var database = Database();
        return await database.Attempts.AsNoTracking()
            .Where(attempt => attempt.Operation!.SessionId == sessionId)
            .Select(attempt => attempt.SessionPublicId)
            .FirstAsync();
    }

    // Other tests leave sessions in the shared database, so the scan result is limited to the session under test.
    private async Task<RefreshSessionEvidenceMessage[]> FindStaleAsync(Guid sessionId)
    {
        await using var database = Database();
        var refresh = new SessionEvidenceRefreshOptions
        {
            MinimumReadInterval = TimeSpan.FromSeconds(30),
            MaximumReadInterval = TimeSpan.FromMinutes(10),
            BatchSize = 1000
        };
        var messages = await FindStaleSessionsMessageHandler.HandleAsync(
            new(),
            database,
            _clock,
            Options.Create(refresh),
            Options.Create(new ChargingFreshnessOptions { FreshnessLimit = FreshnessLimit }),
            CancellationToken.None);
        return [.. messages.OfType<RefreshSessionEvidenceMessage>().Where(message => message.SessionId == sessionId)];
    }

    private async Task RefreshAsync(Guid sessionId, ProviderTransport provider, RecordingStream stream)
    {
        await using var database = Database();
        using var host = CreateProviderHost(database, provider);
        var handler = new RefreshSessionEvidenceMessageHandler(
            database,
            host.Services.GetRequiredService<ChargingProviderClientFactory>(),
            stream,
            _clock,
            Options.Create(new ChargingFreshnessOptions { FreshnessLimit = FreshnessLimit }),
            NullLogger<RefreshSessionEvidenceMessageHandler>.Instance);
        await handler.HandleAsync(new(sessionId), CancellationToken.None);
    }

    private sealed class RecordingStream : IObservationStreamProducer
    {
        public List<ObservationEnvelope> Appended { get; } = [];
        public ObservationStreamException? Failure { get; init; }
        public int Attempts { get; private set; }

        public Task AppendAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Failure is not null)
            {
                throw Failure;
            }

            Appended.AddRange(envelopes);
            return Task.CompletedTask;
        }
    }
}
