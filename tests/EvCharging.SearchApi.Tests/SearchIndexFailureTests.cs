using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EvCharging.SearchApi.Tests;

[Collection(SearchIndexCollection.Name)]
public sealed class SearchIndexFailureTests(SecuredSearchIndexFixture index)
{
    [Fact]
    public async Task Search_WhenTheIndexIsUnreachable_AnswersATypedUnavailableError()
    {
        await using var host = await SearchApiHost.StartAsync("http://search_reader:secret@127.0.0.1:1");

        var response = await host.Client.GetAsync("/search/stations?q=anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("search-unavailable", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_WhenTheIndexDoesNotExistYet_AnswersTheSameTypedError()
    {
        await index.DeleteStationIndicesAsync();
        await using var host = await SearchApiHost.StartAsync(index.ReaderConnectionString);

        var response = await host.Client.GetAsync("/search/stations");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("search-unavailable", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Health_WhenTheIndexIsUnreachable_StaysHealthy()
    {
        await using var host = await SearchApiHost.StartAsync("http://search_reader:secret@127.0.0.1:1");

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_WhenTheIndexFailsOnce_RetriesAndAnswers()
    {
        await using var flaky = await FlakyIndexServer.StartAsync(failures: 1);
        await using var host = await SearchApiHost.StartAsync(flaky.ConnectionString);

        var response = await host.Client.GetAsync("/search/stations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, flaky.Requests);
    }

    [Fact]
    public async Task Search_WhenTheIndexKeepsFailing_StopsAfterTheConfiguredRetries()
    {
        await using var flaky = await FlakyIndexServer.StartAsync(failures: 10);
        await using var host = await SearchApiHost.StartAsync(flaky.ConnectionString);

        var response = await host.Client.GetAsync("/search/stations");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, flaky.Requests);
    }
}
