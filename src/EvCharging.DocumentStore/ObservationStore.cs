using MongoDB.Driver;

namespace EvCharging.DocumentStore;

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
