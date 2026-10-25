using System.Net;
using EvCharging.SearchApi.Features.Stations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EvCharging.SearchApi.Tests;

/// <summary>The search API wired as <c>Program</c> wires it, on a loopback port, against the given index.</summary>
internal sealed class SearchApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SearchApiHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<SearchApiHost> StartAsync(string indexConnectionString)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration["ConnectionStrings:search-reader"] = indexConnectionString;
        builder.Configuration["Search:RequestTimeout"] = "00:00:02";
        builder.Configuration["Search:RetryAttempts"] = "1";
        builder.Configuration["Search:RetryDelay"] = "00:00:00.100";
        builder.AddServiceDefaults();
        builder.AddHttpApiDefaults("/api");
        builder.AddSearchIndexClient();
        builder.Services.AddStationSearch();

        var app = builder.Build();
        app.UseHttpApiDefaults();
        app.MapHttpApiDiagnostics("search-api");
        app.MapStationSearch();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
        return new SearchApiHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
