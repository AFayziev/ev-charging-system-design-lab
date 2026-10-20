using System.Net;
using EvCharging.ChargingWorker.Providers;
using EvCharging.ChargingWorker.Providers.WattHarbor;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

/// <summary>The worker's provider requests through the shared service-default resilience pipeline.</summary>
public sealed class WattHarborResiliencePipelineTests
{
    private const string ClientName = "wattharbor-test";

    [Fact]
    public async Task Start_ProviderThrottles_IsSentOnceAndLeavesTheOutcomeToLookup()
    {
        using var provider = new ScriptedProvider(HttpStatusCode.TooManyRequests);
        using var host = CreateHost(provider);

        var result = await StartAsync(host);

        var error = Assert.IsType<ProviderResponseError>(Assert.Single(result.Errors));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task SustainedFailures_OpenTheCircuit_AndLaterRequestsNeverReachTheProvider()
    {
        const int Requests = 120;
        using var provider = new ScriptedProvider(HttpStatusCode.ServiceUnavailable);
        using var host = CreateHost(provider);
        // A start is never retried by the pipeline, so each one is a single failing sample for the circuit breaker.
        for (int request = 0; request < Requests; request++)
        {
            await StartAsync(host);
        }

        int reached = provider.Calls;
        var start = await StartAsync(host);
        var lookup = await CreateClient(host).GetCommandByExternalOperationIdAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.InRange(reached, 1, Requests - 1);
        Assert.IsType<ProviderTransportError>(Assert.Single(start.Errors));
        Assert.IsType<ProviderTransportError>(Assert.Single(lookup.Errors));
        Assert.Equal(reached, provider.Calls);
    }

    private static Task<Result<ProviderCommandResult>> StartAsync(IHost host) =>
        CreateClient(host).StartAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ProviderEquipmentAddress("station", "charger", "connector"),
            CancellationToken.None);

    private static WattHarborChargingClient CreateClient(IHost host) =>
        new(
            host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName),
            NullLogger<WattHarborChargingClient>.Instance);

    private static IHost CreateHost(ScriptedProvider provider)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri("https://provider.example/"))
            .ConfigurePrimaryHttpMessageHandler(() => provider);
        return builder.Build();
    }

    private sealed class ScriptedProvider(HttpStatusCode status) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
