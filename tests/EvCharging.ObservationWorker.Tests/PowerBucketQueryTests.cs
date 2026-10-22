using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using Xunit;

namespace EvCharging.ObservationWorker.Tests;

public sealed class PowerBucketQueryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Start.AddHours(1);
    private static readonly Guid Session = Guid.NewGuid();
    private readonly ObservationStore _store = fixture.CreateStore();

    [Fact]
    public async Task GetPowerBuckets_ReadingsInTwoMinutes_ReturnsAverageMaximumAndLastEnergyPerBucket()
    {
        await Insert(
            Reading(Start.AddSeconds(10), power: 4m, energy: 1m),
            Reading(Start.AddSeconds(40), power: 8m, energy: 2m),
            Reading(Start.AddSeconds(70), power: 10m, energy: 3m));

        var buckets = await Query(from: null);

        Assert.Equal(
            [
                new PowerBucket(Start, 6m, 8m, 2m),
                new PowerBucket(Start.AddMinutes(1), 10m, 10m, 3m)
            ],
            buckets);
    }

    [Fact]
    public async Task GetPowerBuckets_FromInsideABucket_ReturnsThatBucketWhole()
    {
        await Insert(
            Reading(Start.AddSeconds(10), power: 4m),
            Reading(Start.AddSeconds(50), power: 8m),
            Reading(Start.AddSeconds(70), power: 10m));

        var buckets = await Query(from: Start.AddSeconds(30));

        Assert.Equal(Start, buckets[0].StartsAt);
        Assert.Equal(6m, buckets[0].AveragePowerKw);
        Assert.Equal(2, buckets.Count);
    }

    [Fact]
    public async Task GetPowerBuckets_FromTheLastBucket_ReturnsOnlyThatBucket()
    {
        await Insert(Reading(Start.AddSeconds(10)), Reading(Start.AddSeconds(70)), Reading(Start.AddSeconds(100)));

        var buckets = await Query(from: Start.AddMinutes(1));

        Assert.Equal([Start.AddMinutes(1)], buckets.Select(bucket => bucket.StartsAt));
    }

    [Fact]
    public async Task GetPowerBuckets_ReadingAfterTheLimit_IsLeftOut()
    {
        await Insert(Reading(Start.AddSeconds(10)), Reading(Start.AddMinutes(30)));

        var buckets = await Query(from: null, until: Start.AddMinutes(1));

        Assert.Equal([Start], buckets.Select(bucket => bucket.StartsAt));
    }

    [Fact]
    public async Task GetPowerBuckets_OtherSessionAndProvider_AreLeftOut()
    {
        await Insert(
            Reading(Start.AddSeconds(10), power: 4m),
            Reading(Start.AddSeconds(20), session: Guid.NewGuid(), power: 99m),
            Reading(Start.AddSeconds(30), integrationKey: "provider-b", power: 99m));

        var buckets = await Query(from: null);

        Assert.Equal([new PowerBucket(Start, 4m, 4m, 1m)], buckets);
    }

    [Fact]
    public async Task GetPowerBuckets_NoReadings_ReturnsNothing() => Assert.Empty(await Query(from: null));

    private Task<IReadOnlyList<PowerBucket>> Query(DateTimeOffset? from, DateTimeOffset? until = null) =>
        _store.GetPowerBucketsAsync("provider-a", Session, from, until ?? Later, CancellationToken.None);

    private Task Insert(params ObservationEnvelope[] readings) =>
        _store.History.InsertManyAsync(readings.Select(ObservationHistoryDocument.From));

    private static ObservationEnvelope Reading(
        DateTimeOffset at,
        decimal power = 7m,
        decimal energy = 1m,
        Guid? session = null,
        string integrationKey = "provider-a") => new(
            ObservationEnvelope.CurrentSchemaVersion,
            Guid.NewGuid(),
            integrationKey,
            Guid.NewGuid(),
            session ?? Session,
            "station",
            "charger",
            "connector",
            at,
            at,
            ObservationState.Charging,
            power,
            energy,
            false);
}
