using Xunit;

namespace EvCharging.ObjectStorage.Tests;

[CollectionDefinition(Name)]
public sealed class ObjectStorageCollection : ICollectionFixture<ProvisionedStorageFixture>
{
    public const string Name = "Provisioned object storage";
}
