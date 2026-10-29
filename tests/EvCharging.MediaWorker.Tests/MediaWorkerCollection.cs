using EvCharging.Testing;
using Xunit;

namespace EvCharging.MediaWorker.Tests;

[CollectionDefinition(Name)]
public sealed class MediaWorkerCollection
    : ICollectionFixture<ObjectStoreFixture>, ICollectionFixture<PlatformDatabaseFixture>
{
    public const string Name = "Media worker";
}
