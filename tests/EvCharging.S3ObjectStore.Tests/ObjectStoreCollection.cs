using EvCharging.Testing;
using Xunit;

namespace EvCharging.S3ObjectStore.Tests;

[CollectionDefinition(Name)]
public sealed class ObjectStoreCollection : ICollectionFixture<ObjectStoreFixture>
{
    public const string Name = "Provisioned object store";
}
