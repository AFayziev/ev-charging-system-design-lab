using Xunit;

namespace EvCharging.SearchApi.Tests;

[CollectionDefinition(Name)]
public sealed class SearchIndexCollection : ICollectionFixture<SecuredSearchIndexFixture>
{
    public const string Name = "Secured search index";
}
