using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvCharging.SearchApi.Tests;

/// <summary>An index endpoint that answers searches with 503 a set number of times, then with no hits.</summary>
internal sealed class FlakyIndexServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _failuresLeft;

    private FlakyIndexServer(WebApplication app, int failures, string connectionString)
    {
        _app = app;
        _failuresLeft = failures;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public int Requests { get; private set; }

    public static async Task<FlakyIndexServer> StartAsync(int failures)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        FlakyIndexServer? server = null;
        app.Run(async context =>
        {
            context.Response.Headers["X-Elastic-Product"] = "Elasticsearch";
            server!.Requests++;
            if (server._failuresLeft-- > 0)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"took":1,"timed_out":false,"hits":{"hits":[]}}""");
        });
        await app.StartAsync();
        var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Single());
        server = new FlakyIndexServer(app, failures, $"http://search_reader:secret@{address.Authority}");
        return server;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
