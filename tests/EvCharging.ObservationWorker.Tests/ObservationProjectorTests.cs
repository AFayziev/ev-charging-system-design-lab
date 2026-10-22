using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.ObservationWorker.Projection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace EvCharging.ObservationWorker.Tests;

public sealed class ObservationProjectorTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Session = Guid.NewGuid();
    private readonly ObservationStore _store = fixture.CreateStore();

    [Fact]
    public async Task Project_FirstReading_StoresHistoryAndLatestWithExactValues()
    {
        var reading = Reading(at: Start.AddSeconds(2), power: 7.25m, energy: 12.345m);

        await Project(reading);

        var latest = await LatestAsync();
        Assert.Equal(reading.SourceEventId, latest.SourceEventId);
        Assert.Equal(reading.OccurredAt, latest.Reading.OccurredAt);
        Assert.Equal(7.25m, latest.Reading.PowerKw);
        Assert.Equal(12.345m, latest.Reading.EnergyKWh);
        Assert.Equal(ObservationState.Charging, latest.Reading.State);
        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_DuplicateDelivery_KeepsOneHistoryDocument()
    {
        var reading = Reading(at: Start);

        await Project(reading);
        await Project(reading, reading);

        Assert.Equal(1, await HistoryCountAsync());
        Assert.Equal(reading.SourceEventId, (await LatestAsync()).SourceEventId);
    }

    [Fact]
    public async Task Project_OlderReading_IsHistoryOnly()
    {
        var newer = Reading(at: Start.AddSeconds(10), energy: 5m);
        var older = Reading(at: Start.AddSeconds(4), energy: 2m);

        await Project(newer);
        await Project(older);

        Assert.Equal(newer.SourceEventId, (await LatestAsync()).SourceEventId);
        Assert.Equal(2, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_SameOccurrenceTime_KeepsTheFirstLatest()
    {
        var first = Reading(at: Start);
        var tie = Reading(at: Start, energy: 9m);

        await Project(first);
        await Project(tie);

        Assert.Equal(first.SourceEventId, (await LatestAsync()).SourceEventId);
        Assert.Equal(2, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_UnorderedBatch_StoresTheNewestAsLatest()
    {
        var oldest = Reading(at: Start);
        var middle = Reading(at: Start.AddSeconds(2));
        var newest = Reading(at: Start.AddSeconds(4));

        await Project(middle, newest, oldest);

        Assert.Equal(newest.SourceEventId, (await LatestAsync()).SourceEventId);
        Assert.Equal(3, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_FutureDatedReading_IsHistoryOnlyAndDoesNotBlockLaterReadings()
    {
        var ahead = Reading(at: Start.AddHours(1), received: Start);
        var current = Reading(at: Start.AddSeconds(2), received: Start.AddSeconds(2));

        await Project(ahead);
        Assert.Null(await FindLatestAsync());

        await Project(current);

        Assert.Equal(current.SourceEventId, (await LatestAsync()).SourceEventId);
        Assert.Equal(2, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_RedeliveryAfterHistoryWrite_RepairsTheLatestView()
    {
        var reading = Reading(at: Start);
        // The worker stopped after the history write.
        await _store.History.InsertOneAsync(ObservationHistoryDocument.From(reading));

        await Project(reading);

        Assert.Equal(reading.SourceEventId, (await LatestAsync()).SourceEventId);
        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task Project_ProvidersSharingASessionId_KeepSeparateViews()
    {
        var first = Reading(at: Start, integrationKey: "provider-a");
        var second = Reading(at: Start.AddSeconds(2), integrationKey: "provider-b");

        await Project(first, second);

        Assert.Equal(first.SourceEventId, (await LatestAsync("provider-a")).SourceEventId);
        Assert.Equal(second.SourceEventId, (await LatestAsync("provider-b")).SourceEventId);
    }

    private Task Project(params ObservationEnvelope[] envelopes) =>
        new ObservationProjector(
            _store,
            Options.Create(new ObservationClockSkewOptions { ClockSkew = TimeSpan.FromSeconds(5) }),
            NullLogger<ObservationProjector>.Instance)
            .HandleAsync(envelopes, CancellationToken.None);

    private Task<long> HistoryCountAsync() =>
        _store.History.CountDocumentsAsync(FilterDefinition<ObservationHistoryDocument>.Empty);

    private async Task<SessionLatestDocument> LatestAsync(string integrationKey = "provider-a") =>
        await FindLatestAsync(integrationKey) ?? throw new InvalidOperationException("No latest reading.");

    private async Task<SessionLatestDocument?> FindLatestAsync(string integrationKey = "provider-a") =>
        await _store.Latest
            .Find(document => document.Id.IntegrationKey == integrationKey && document.Id.ExternalSessionId == Session)
            .FirstOrDefaultAsync();

    private static ObservationEnvelope Reading(
        DateTimeOffset at,
        DateTimeOffset? received = null,
        decimal power = 7m,
        decimal energy = 1m,
        string integrationKey = "provider-a") => new(
            ObservationEnvelope.CurrentSchemaVersion,
            Guid.NewGuid(),
            integrationKey,
            Guid.NewGuid(),
            Session,
            "station",
            "charger",
            "connector",
            at,
            received ?? at,
            ObservationState.Charging,
            power,
            energy,
            false);
}
