using System.Net;
using System.Text.Json;
using EvCharging.ChargingWorker.Providers;
using EvCharging.ChargingWorker.Providers.Synchronous;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed class SynchronousChargingProviderClientTests
{
    [Theory]
    [InlineData("mismatched-reference")]
    [InlineData("unknown-outcome")]
    [InlineData("missing-fields")]
    [InlineData("unrecognized-rejection")]
    [InlineData("rejection-on-server-error")]
    public async Task Start_UnrecognizedEvidence_PreservesResponseWithoutConfirmingOrRejecting(string scenario)
    {
        var reference = Guid.NewGuid();
        var payload = new CommandPayload(
            Guid.NewGuid(),
            reference,
            "applied",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null);
        var status = scenario is "unrecognized-rejection" ? HttpStatusCode.Conflict
            : scenario is "rejection-on-server-error" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
        var body = scenario switch
        {
            "mismatched-reference" => JsonSerializer.Serialize(payload with { Reference = Guid.NewGuid() }),
            "unknown-outcome" => JsonSerializer.Serialize(payload with { Outcome = "new-provider-state" }),
            "missing-fields" => "{}",
            _ => JsonSerializer.Serialize(new ProblemPayload(
                (int)status,
                scenario == "unrecognized-rejection" ? "new-provider-error" : "connector_unavailable",
                Guid.NewGuid(),
                reference))
        };
        using var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new("application/problem+json");
        using var handler = new ResponseHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.example/") };
        var adapter = new SynchronousChargingProviderClient(
            http,
            NullLogger<SynchronousChargingProviderClient>.Instance);
        var result = await adapter.StartAsync(
            reference,
            Guid.NewGuid(),
            new ProviderEquipmentAddress("station", "charger", "connector"),
            CancellationToken.None);
        var error = Assert.IsType<ProviderResponseError>(Assert.Single(result.Errors));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(body, error.ResponseBody);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("connection", false)]
    [InlineData("timeout", true)]
    [InlineData("circuit-open", false)]
    [InlineData("rate-limit", false)]
    public async Task Start_TransportFailure_DoesNotBecomeARejection(string scenario, bool timedOut)
    {
        Exception failure = scenario switch
        {
            "timeout" => new TimeoutRejectedException(),
            "circuit-open" => new BrokenCircuitException(),
            "rate-limit" => new RateLimiterRejectedException(),
            _ => new HttpRequestException("Connection lost.")
        };
        using var handler = new ResponseHandler(_ => throw failure);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.example/") };
        var adapter = new SynchronousChargingProviderClient(
            http,
            NullLogger<SynchronousChargingProviderClient>.Instance);
        var result = await adapter.StartAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ProviderEquipmentAddress("station", "charger", "connector"),
            CancellationToken.None);
        var error = Assert.IsType<ProviderTransportError>(Assert.Single(result.Errors));
        Assert.Equal(timedOut, error.TimedOut);
        Assert.Same(
            failure,
            Assert.IsAssignableFrom<FluentResults.IExceptionalError>(
                Assert.Single(error.Reasons)).Exception);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Start_CallerCancellation_RemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new ResponseHandler(async token =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The request should have been cancelled.");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.example/") };
        var adapter = new SynchronousChargingProviderClient(
            http,
            NullLogger<SynchronousChargingProviderClient>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StartAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ProviderEquipmentAddress("station", "charger", "connector"),
            cancellation.Token));
    }

    private sealed class ResponseHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(cancellationToken);
        }
    }

    private sealed record CommandPayload(
        Guid Id,
        Guid Reference,
        string Outcome,
        Guid? SessionId,
        DateTimeOffset? AppliedAt,
        string? Rejection);
    private sealed record ProblemPayload(int Status, string Code, Guid CommandId, Guid Reference);
}
