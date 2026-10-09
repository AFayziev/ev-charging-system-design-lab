using Xunit;

namespace EvCharging.E2E.Tests;

[CollectionDefinition(nameof(E2ECollection))]
public sealed class E2ECollection : ICollectionFixture<AppHostFixture>;
