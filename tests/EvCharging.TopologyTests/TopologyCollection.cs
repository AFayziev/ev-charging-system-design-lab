using Xunit;

namespace EvCharging.TopologyTests;

[CollectionDefinition("Topology")]
public sealed class TopologyCollection : ICollectionFixture<AppHostFixture>;
