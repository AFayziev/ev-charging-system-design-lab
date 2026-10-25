using EvCharging.Testing;
using Xunit;

namespace EvCharging.SearchIndexWorker.Tests;

[CollectionDefinition(Name)]
public sealed class SearchIndexCollection
    : ICollectionFixture<ElasticsearchFixture>, ICollectionFixture<PlatformDatabaseFixture>
{
    public const string Name = "Search index";
}
