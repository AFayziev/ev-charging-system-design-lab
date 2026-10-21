using EvCharging.DocumentStore;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace EvCharging.ObservationWorker.Tests;

public sealed class MongoDbFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.3.11-noble").Build();
    private IMongoClient? _client;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _mongo.StartAsync(timeout.Token);
        _client = new MongoClient(_mongo.GetConnectionString());
    }

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

    /// <summary>A store over a database no other test touches.</summary>
    public ObservationStore CreateStore() =>
        new((_client ?? throw new InvalidOperationException("The fixture is not started.")).GetDatabase(
            $"t{Guid.NewGuid():N}"));
}
