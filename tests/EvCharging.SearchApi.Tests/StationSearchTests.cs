using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.SearchApi.Features.Stations;
using EvCharging.SearchIndex;
using Xunit;
using static EvCharging.SearchApi.Tests.StationDocuments;

namespace EvCharging.SearchApi.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class StationSearchTests(SecuredSearchIndexFixture index) : IAsyncLifetime
{
    // Riverside and Oxford Street are central, Canary Wharf is 7 km east, Heathrow 23 km west.
    private readonly StationSearchDocument _riverside = Station(
        "Riverside Hub",
        "1 Embankment Road",
        51.50,
        -0.12,
        Connector("Type2", "AC", 22),
        Connector("CCS2", "DC", 50));

    private readonly StationSearchDocument _canaryWharf = Station(
        "Canary Wharf Rapid", "5 Docklands Quay", 51.505, -0.02, Connector("CCS2", "DC", 150));

    private readonly StationSearchDocument _heathrow = Station(
        "Heathrow Park", "Terminal Road", 51.47, -0.45, Connector("Type2", "AC", 7));

    private readonly StationSearchDocument _oxfordStreet = Station(
        "Oxford Street Charge",
        "9 Shopping Mile",
        51.515,
        -0.14,
        Connector("Type2", "AC", 7),
        Connector("CCS2", "DC", 150));

    private SearchApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await index.ResetIndexAsync(_riverside, _canaryWharf, _heathrow, _oxfordStreet);
        _host = await SearchApiHost.StartAsync(index.ReaderConnectionString);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Search_WithoutParameters_ListsEveryStationByName()
    {
        var response = await SearchAsync("");

        Assert.Equal(
            [_canaryWharf.PublicId, _heathrow.PublicId, _oxfordStreet.PublicId, _riverside.PublicId],
            response.Stations.Select(station => station.Id));
        Assert.All(response.Stations, station => Assert.Null(station.DistanceKm));
        Assert.Null(response.NextCursor);
    }

    [Fact]
    public async Task Search_BlankText_BehavesLikeNoText()
    {
        var response = await SearchAsync("q=%20&limit=1");

        Assert.Equal(_canaryWharf.PublicId, response.Stations.Single().Id);
        Assert.NotNull(response.NextCursor);
        var next = await SearchAsync($"cursor={response.NextCursor}&limit=1");
        Assert.Equal(_heathrow.PublicId, next.Stations.Single().Id);
    }

    [Fact]
    public async Task Search_TinyRadius_IsAcceptedWithoutExponentNotation()
    {
        var response = await SearchAsync("lat=51.50&lon=-0.12&radiusKm=0.001");

        Assert.Equal([_riverside.PublicId], response.Stations.Select(station => station.Id));
    }

    [Fact]
    public async Task Search_ReturnsTheBrowseShape()
    {
        var response = await SearchAsync("q=riverside");

        var station = Assert.Single(response.Stations);
        Assert.Equal("Riverside Hub", station.Name);
        Assert.Equal("1 Embankment Road", station.Address);
        Assert.Equal((1, 2), (station.ChargerCount, station.ConnectorCount));
        Assert.Equal(
            [new ConnectorCapabilityResponse("CCS2", "DC", 50), new ConnectorCapabilityResponse("Type2", "AC", 22)],
            station.Capabilities);
    }

    [Theory]
    [InlineData("riverside", "Riverside Hub")]
    [InlineData("riversde", "Riverside Hub")]
    [InlineData("docklands", "Canary Wharf Rapid")]
    public async Task Search_Text_MatchesNameAndAddressAllowingTypos(string text, string expectedName)
    {
        var response = await SearchAsync($"q={text}");

        Assert.Equal(expectedName, response.Stations.First().Name);
    }

    [Theory]
    [InlineData("connectorType=CCS2", "Canary Wharf Rapid|Oxford Street Charge|Riverside Hub")]
    [InlineData(
        "connectorType=CCS2&connectorType=Type2",
        "Canary Wharf Rapid|Heathrow Park|Oxford Street Charge|Riverside Hub")]
    [InlineData("powerSupply=AC", "Heathrow Park|Oxford Street Charge|Riverside Hub")]
    [InlineData("minPowerKw=100", "Canary Wharf Rapid|Oxford Street Charge")]
    public async Task Search_CapabilityFilters_KeepMatchingStations(string query, string expectedNames)
    {
        var response = await SearchAsync(query);

        Assert.Equal(expectedNames.Split('|'), response.Stations.Select(station => station.Name));
    }

    [Theory]
    [InlineData("connectorType=CCS2&powerSupply=AC")]
    [InlineData("connectorType=Type2&minPowerKw=100")]
    public async Task Search_CapabilityFilters_MustHoldOnOneConnector(string query)
    {
        var response = await SearchAsync(query);

        Assert.Empty(response.Stations);
    }

    [Fact]
    public async Task Search_ByLocation_OrdersNearestFirstWithDistance()
    {
        var response = await SearchAsync("lat=51.50&lon=-0.12");

        Assert.Equal(
            [_riverside.PublicId, _oxfordStreet.PublicId, _canaryWharf.PublicId, _heathrow.PublicId],
            response.Stations.Select(station => station.Id));
        Assert.InRange(response.Stations[0].DistanceKm!.Value, 0, 0.1);
        Assert.InRange(response.Stations[1].DistanceKm!.Value, 1, 3);
        Assert.InRange(response.Stations[2].DistanceKm!.Value, 6, 8);
        Assert.InRange(response.Stations[3].DistanceKm!.Value, 20, 26);
    }

    [Fact]
    public async Task Search_WithRadius_KeepsStationsWithinIt()
    {
        var response = await SearchAsync("lat=51.50&lon=-0.12&radiusKm=5");

        Assert.Equal(
            [_riverside.PublicId, _oxfordStreet.PublicId],
            response.Stations.Select(station => station.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("q=hub%20road%20park%20street%20rapid%20quay%20mile%20terminal%20embankment")]
    [InlineData("lat=51.50&lon=-0.12")]
    public async Task Search_Paging_VisitsEveryMatchOnceInOrder(string query)
    {
        var all = await SearchAsync(string.IsNullOrEmpty(query) ? "limit=50" : $"{query}&limit=50");
        var visited = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await SearchAsync(
                cursor is null ? (query.Length == 0 ? "limit=1" : $"{query}&limit=1") : $"cursor={cursor}&limit=1");
            Assert.True(page.Stations.Count <= 1);
            visited.AddRange(page.Stations.Select(station => station.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(all.Stations.Select(station => station.Id), visited);
        Assert.Equal(visited.Count, visited.Distinct().Count());
    }

    [Theory]
    [InlineData("lat=51.5", "Lon")]
    [InlineData("lat=91&lon=0", "Lat")]
    [InlineData("radiusKm=5", "RadiusKm")]
    [InlineData("lat=51.5&lon=0&radiusKm=100000", "RadiusKm")]
    [InlineData("lat=51.5&lon=0&radiusKm=0.0001", "RadiusKm")]
    [InlineData("minPowerKw=-1", "MinPowerKw")]
    [InlineData("limit=0", "Limit")]
    [InlineData("cursor=not-a-cursor", "Cursor")]
    [InlineData("connectorType=%20", "ConnectorType")]
    public async Task Search_InvalidParameters_AreRejectedByName(string query, string property)
    {
        var response = await _host.Client.GetAsync($"/search/stations?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(property, out _), problem.ToString());
    }

    [Fact]
    public async Task Search_Cursor_ContinuesItsOwnSearchWhateverTheNextPageSize()
    {
        var first = await SearchAsync("connectorType=CCS2&lat=51.50&lon=-0.12&limit=1");

        var rest = await SearchAsync($"cursor={first.NextCursor}&limit=50");

        Assert.Equal(
            [_oxfordStreet.PublicId, _canaryWharf.PublicId],
            rest.Stations.Select(station => station.Id));
        Assert.NotNull(rest.Stations[0].DistanceKm);
        Assert.Null(rest.NextCursor);
    }

    [Fact]
    public async Task Search_CursorWithSearchParameters_IsRejected()
    {
        var page = await SearchAsync("limit=1");

        var response = await _host.Client.GetAsync($"/search/stations?cursor={page.NextCursor}&q=riverside");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Cursor", out _), problem.ToString());
    }

    [Theory]
    [InlineData("""{"request":{"lat":200,"lon":0},"after":[1.0,"00000000-0000-0000-0000-000000000001"]}""")]
    [InlineData("""{"request":{"q":"a"},"after":["name","00000000-0000-0000-0000-000000000001"]}""")]
    [InlineData("""{"after":[1.0,"00000000-0000-0000-0000-000000000001"]}""")]
    public async Task Search_CraftedCursor_IsValidatedLikeAnyRequest(string payload)
    {
        var cursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(payload));

        var response = await _host.Client.GetAsync($"/search/stations?cursor={cursor}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<StationSearchResponse> SearchAsync(string query)
    {
        var response = await _host.Client.GetAsync($"/search/stations?{query}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StationSearchResponse>(JsonSerializerOptions.Web))!;
    }
}
