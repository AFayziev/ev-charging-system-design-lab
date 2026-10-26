using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.SearchApi.Features.Stations;
using Xunit;
using static EvCharging.SearchApi.Tests.StationDocuments;

namespace EvCharging.SearchApi.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class StationSearchOptionsTests(SecuredSearchIndexFixture index) : IAsyncLifetime
{
    private SearchApiHost _host = null!;

    public async Task InitializeAsync() => _host = await SearchApiHost.StartAsync(index.ReaderConnectionString);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Options_ListEveryDistinctValueTheIndexHoldsSorted()
    {
        await index.ResetIndexAsync(
            Station("First", "1 Road", 51.5, -0.1, Connector("Type2", "AC", 22), Connector("CCS2", "DC", 50)),
            Station("Second", "2 Road", 51.6, -0.2, Connector("CCS2", "DC", 150), Connector("CHAdeMO", "DC", 7.4m)),
            Station("Third", "3 Road", 51.7, -0.3, Connector("Type2", "AC", 22)));

        var options = await GetOptionsAsync();

        Assert.Equal(["CCS2", "CHAdeMO", "Type2"], options.ConnectorTypes);
        Assert.Equal(["AC", "DC"], options.PowerSupplies);
        Assert.Equal([7.4m, 22m, 50m, 150m], options.RatedPowersKw);
    }

    [Fact]
    public async Task Options_WhenNoStationIsIndexed_AreEmpty()
    {
        await index.ResetIndexAsync();

        var options = await GetOptionsAsync();

        Assert.Empty(options.ConnectorTypes);
        Assert.Empty(options.PowerSupplies);
        Assert.Empty(options.RatedPowersKw);
    }

    private async Task<StationSearchOptionsResponse> GetOptionsAsync()
    {
        var response = await _host.Client.GetAsync("/search/stations/options");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StationSearchOptionsResponse>(JsonSerializerOptions.Web))!;
    }
}
