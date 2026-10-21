using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvCharging.ProviderContracts.Tests;

/// <summary>Stands in for the platform: records the provider's observation callbacks and can fail them.</summary>
public sealed class CallbackReceiver : IAsyncDisposable
{
    public const string ApiKey = "contract-test-callback-key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly WebApplication _app;
    private readonly List<ReceivedObservation> _received = [];
    private int _status = 202;
    private int _requestCount;

    private CallbackReceiver(WebApplication app, Uri url)
    {
        _app = app;
        Url = url;
    }

    public Uri Url { get; }

    public static async Task<CallbackReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        CallbackReceiver? receiver = null;
        app.MapPost(
            "/observations",
            async (HttpRequest request) =>
            {
                var batch = await request.ReadFromJsonAsync<Batch>(Json)
                    ?? throw new InvalidOperationException("A callback body is required.");
                var apiKey = request.Headers["X-Api-Key"].ToString();
                var receivedAt = DateTimeOffset.UtcNow;
                lock (receiver!._received)
                {
                    int requestId = ++receiver._requestCount;
                    // One request's observations are recorded together, in the order they were reported.
                    receiver._received.AddRange(batch.Observations.Select(payload => new ReceivedObservation(
                        payload.EventId,
                        payload.ExternalSessionId,
                        payload.OccurredAt,
                        payload.State,
                        payload.PowerKw,
                        payload.EnergyWh,
                        payload.IsFinalEnergy,
                        apiKey,
                        receivedAt,
                        requestId)));
                }

                return Results.StatusCode(Volatile.Read(ref receiver._status));
            });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            .Single() ?? throw new InvalidOperationException("The receiver has no address.");
        receiver = new CallbackReceiver(app, new Uri(new Uri(address), "/observations"));
        return receiver;
    }

    /// <summary>Makes every callback answer with a status; 202 makes them succeed again.</summary>
    public void RespondWith(int statusCode) => Volatile.Write(ref _status, statusCode);

    /// <summary>Callbacks received for a session, in arrival order.</summary>
    public IReadOnlyList<ReceivedObservation> For(Guid externalSessionId)
    {
        lock (_received)
        {
            return _received.Where(observation => observation.ExternalSessionId == externalSessionId).ToArray();
        }
    }

    public async Task<IReadOnlyList<ReceivedObservation>> WaitForAsync(
        Guid externalSessionId, Func<IReadOnlyList<ReceivedObservation>, bool> condition, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var observations = For(externalSessionId);
            if (condition(observations))
            {
                return observations;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {description}; {observations.Count} received, "
                    + $"longest delivery lag {LongestLag()}.");
            }
        }
    }

    /// <summary>The longest wait between a reading's occurrence and its arrival, across all sessions.</summary>
    private TimeSpan LongestLag()
    {
        lock (_received)
        {
            return _received.Select(observation => observation.ReceivedAt - observation.OccurredAt)
                .DefaultIfEmpty()
                .Max();
        }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private sealed record Batch(IReadOnlyList<Payload> Observations);

    private sealed record Payload(
        Guid EventId,
        Guid ExternalSessionId,
        DateTimeOffset OccurredAt,
        string State,
        decimal PowerKw,
        decimal EnergyWh,
        bool IsFinalEnergy);
}

public sealed record ReceivedObservation(
    Guid EventId,
    Guid ExternalSessionId,
    DateTimeOffset OccurredAt,
    string State,
    decimal PowerKw,
    decimal EnergyWh,
    bool IsFinalEnergy,
    string ApiKey,
    DateTimeOffset ReceivedAt,
    int RequestId);
