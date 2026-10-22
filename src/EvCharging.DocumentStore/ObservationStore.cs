using MongoDB.Bson;
using MongoDB.Driver;

namespace EvCharging.DocumentStore;

/// <summary>A session's power over one bucket of source time.</summary>
public sealed record PowerBucket(
    DateTimeOffset StartsAt, decimal AveragePowerKw, decimal MaximumPowerKw, decimal LastEnergyKWh);

/// <summary>The collections the observation consumers write and the API reads.</summary>
public sealed class ObservationStore(IMongoDatabase database)
{
    // An observation is acknowledged to Kafka only after these writes are journaled by a majority.
    private static readonly WriteConcern Durable = new(WriteConcern.WMode.Majority, journal: true);

    public IMongoCollection<ObservationHistoryDocument> History { get; } = database
        .GetCollection<ObservationHistoryDocument>("observation_history")
        .WithWriteConcern(Durable);

    public IMongoCollection<SessionLatestDocument> Latest { get; } = database
        .GetCollection<SessionLatestDocument>("session_latest")
        .WithWriteConcern(Durable);

    /// <summary>
    /// Averages a session's power per minute of source time, for readings from the start of the minute holding
    /// <paramref name="from"/> up to <paramref name="until"/>. Asking from the last bucket already held returns that
    /// bucket whole, so a caller that is up to date receives one bucket.
    /// </summary>
    public async Task<IReadOnlyList<PowerBucket>> GetPowerBucketsAsync(
        string integrationKey,
        Guid externalSessionId,
        DateTimeOffset? from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var filters = Builders<ObservationHistoryDocument>.Filter;
        var match = filters.Eq(document => document.IntegrationKey, integrationKey)
            & filters.Eq(document => document.ExternalSessionId, externalSessionId)
            & filters.Lte(document => document.Reading.OccurredAt, until);
        if (from is { } start)
        {
            match &= filters.Gte(document => document.Reading.OccurredAt, FloorToMinute(start));
        }

        // Element names follow the property names; $dateTrunc has no LINQ translation, so the grouping is a stage.
        var buckets = await History.Aggregate()
            .Match(match)
            .SortBy(document => document.Reading.OccurredAt)
            .AppendStage<BsonDocument>(new BsonDocument(
                "$group",
                new BsonDocument
                {
                    ["_id"] = new BsonDocument(
                        "$dateTrunc",
                        new BsonDocument
                        {
                            ["date"] = "$Reading.OccurredAt",
                            ["unit"] = "minute"
                        }),
                    ["average"] = new BsonDocument("$avg", "$Reading.PowerKw"),
                    ["maximum"] = new BsonDocument("$max", "$Reading.PowerKw"),
                    ["energy"] = new BsonDocument("$last", "$Reading.EnergyKWh")
                }))
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync(cancellationToken);
        return buckets
            .Select(value => new PowerBucket(
                new DateTimeOffset(value["_id"].ToUniversalTime(), TimeSpan.Zero),
                value["average"].ToDecimal(),
                value["maximum"].ToDecimal(),
                value["energy"].ToDecimal()))
            .ToList();
    }

    private static DateTimeOffset FloorToMinute(DateTimeOffset instant) =>
        new(instant.UtcTicks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero);

    /// <summary>Creates the indexes the queries rely on; safe to repeat.</summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        var byOccurrence = Builders<ObservationHistoryDocument>.IndexKeys
            .Ascending(document => document.IntegrationKey)
            .Ascending(document => document.ExternalSessionId)
            .Ascending(document => document.Reading.OccurredAt);
        await History.Indexes.CreateOneAsync(
            new CreateIndexModel<ObservationHistoryDocument>(
                byOccurrence,
                new CreateIndexOptions
                {
                    Name = "observation_history_by_session_time"
                }),
            cancellationToken: cancellationToken);
    }
}
