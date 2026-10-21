using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Features.Observations;
using EvCharging.Api.Streaming;
using EvCharging.Contracts.Observations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class ObservationIngestionTests
{
    private const string ApiKey = "wattharbor-test-key";
    private const string OtherClientKey = "amptrail-test-key";
    private const string IntegrationKey = "wattharbor-lab";
    private static readonly Guid EventId = Guid.Parse("0c0e5e57-3a0b-4b53-a2ff-5f1f0b1d0d01");
    private static readonly Guid SessionId = Guid.Parse("7a1f6a8e-6a58-4d4e-8c6b-0b1b8f2c9a02");

    [Fact]
    public async Task ReportObservation_WithoutOrWithWrongKey_IsRejectedBeforeAppend()
    {
        await using var host = await ObservationHost.StartAsync();

        using var anonymous = await host.PostAsync(Body(), apiKey: null);
        using var wrong = await host.PostAsync(Body(), "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Empty(host.Stream.Appended);
    }

    [Fact]
    public async Task ReportObservation_KeyOfAnotherClient_IsForbidden()
    {
        await using var host = await ObservationHost.StartAsync();

        using var response = await host.PostAsync(Body(), OtherClientKey);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Stream.Appended);
    }

    [Fact]
    public async Task ReportObservation_ValidPayload_AppendsNormalizedEnvelopeBeforeAcknowledging()
    {
        await using var host = await ObservationHost.StartAsync();
        var body = Body(energyWh: 12_500, occurredAt: "2026-10-21T10:00:00+05:00");

        using var response = await host.PostAsync(body, ApiKey);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<ObservationAcceptedResponse>();
        var envelope = Assert.Single(host.Stream.Appended);
        Assert.Equal(accepted?.ObservationId, envelope.ObservationId);
        Assert.Equal(ObservationEnvelope.CurrentSchemaVersion, envelope.SchemaVersion);
        Assert.Equal(IntegrationKey, envelope.IntegrationKey);
        Assert.Equal(EventId, envelope.SourceEventId);
        Assert.Equal(SessionId, envelope.ExternalSessionId);
        Assert.Equal(12.5m, envelope.EnergyKWh);
        Assert.Equal(7.4m, envelope.PowerKw);
        Assert.Equal(ObservationState.Charging, envelope.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero), envelope.OccurredAt);
        Assert.Equal(TimeSpan.Zero, envelope.ReceivedAt.Offset);
        Assert.Equal(body, envelope.RawPayload);
    }

    [Fact]
    public async Task ReportObservation_RepeatedEvent_AppendsEachDeliveryAndAcknowledges()
    {
        await using var host = await ObservationHost.StartAsync();
        var body = Body();

        using var first = await host.PostAsync(body, ApiKey);
        using var second = await host.PostAsync(body, ApiKey);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(2, host.Stream.Appended.Count);
        Assert.All(host.Stream.Appended, envelope => Assert.Equal(EventId, envelope.SourceEventId));
    }

    [Fact]
    public async Task ReportObservation_UnconfirmedAppend_IsNotAcknowledged()
    {
        await using var host = await ObservationHost.StartAsync();
        host.Stream.Failure = new ObservationStreamException("down", new InvalidOperationException("broker"));

        using var response = await host.PostAsync(Body(), ApiKey);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Stream.Appended);
    }

    [Theory]
    [InlineData("powerKw", -1, "PowerKw")]
    [InlineData("energyWh", -1, "EnergyWh")]
    [InlineData("stationId", "", "StationId")]
    [InlineData("eventId", "00000000-0000-0000-0000-000000000000", "EventId")]
    [InlineData("occurredAt", "0001-01-01T00:00:00+00:00", "OccurredAt")]
    [InlineData("isFinalEnergy", true, "IsFinalEnergy")]
    public async Task ReportObservation_InvalidPayload_IsRejectedWithFieldErrors(
        string field, object value, string expectedError)
    {
        await using var host = await ObservationHost.StartAsync();
        var body = Body(field, value);

        using var response = await host.PostAsync(body, ApiKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            problem.GetProperty("errors").EnumerateObject(),
            error => string.Equals(error.Name, expectedError, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(host.Stream.Appended);
    }

    [Fact]
    public async Task ReportObservation_IncompleteOrOversizedPayload_IsRejected()
    {
        await using var host = await ObservationHost.StartAsync();
        var incomplete = """{"eventId":"0c0e5e57-3a0b-4b53-a2ff-5f1f0b1d0d01"}""";
        var oversized = Body("stationId", new string('x', 20_000));

        using var missing = await host.PostAsync(incomplete, ApiKey);
        using var large = await host.PostAsync(oversized, ApiKey);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Empty(host.Stream.Appended);
    }

    private static string Body(
        string? field = null,
        object? value = null,
        decimal energyWh = 5_000,
        string occurredAt = "2026-10-21T10:00:00Z")
    {
        var values = new Dictionary<string, object?>
        {
            ["eventId"] = EventId,
            ["externalSessionId"] = SessionId,
            ["stationId"] = "station-1",
            ["chargerId"] = "charger-1",
            ["connectorId"] = "connector-1",
            ["occurredAt"] = occurredAt,
            ["state"] = "charging",
            ["powerKw"] = 7.4m,
            ["energyWh"] = energyWh,
            ["isFinalEnergy"] = false
        };
        if (field is not null)
        {
            values[field] = value;
        }

        return JsonSerializer.Serialize(values);
    }

    private sealed class FakeObservationStream : IObservationStream
    {
        public List<ObservationEnvelope> Appended { get; } = [];

        public Exception? Failure { get; set; }

        public Task AppendAsync(ObservationEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Appended.Add(envelope);
            return Task.CompletedTask;
        }
    }

    private sealed class ObservationHost(WebApplication app, HttpClient client, FakeObservationStream stream)
        : IAsyncDisposable
    {
        public FakeObservationStream Stream { get; } = stream;

        public static async Task<ObservationHost> StartAsync()
        {
            var stream = new FakeObservationStream();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
            builder.Configuration["ApiAuthentication:Authority"] = "https://identity.example.test/realms/evcharging";
            builder.Configuration["ApiAuthentication:MetadataAddress"] =
                "https://identity.example.test/realms/evcharging/.well-known/openid-configuration";
            builder.Configuration["ApiAuthentication:Audience"] = "evcharging-api";
            builder.Configuration["ApiAuthentication:AllowLocalHttp"] = "false";
            builder.Configuration[$"ObservationSources:Sources:{IntegrationKey}:Client"] = "WattHarbor";
            builder.Configuration[$"ObservationSources:Sources:{IntegrationKey}:ApiKeys:0"] = ApiKey;
            builder.Configuration["ObservationSources:Sources:amptrail-lab:Client"] = "AmpTrail";
            builder.Configuration["ObservationSources:Sources:amptrail-lab:ApiKeys:0"] = OtherClientKey;
            builder.AddApiAuthentication();
            builder.AddObservationSourceAuthentication();
            builder.AddHttpApiDefaults();
            builder.Services.AddApplicationValidation();
            builder.Services.AddSingleton<IObservationStream>(stream);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.UseWolverine(options =>
            {
                options.AddApplicationHandlers();
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.Discovery.DisableConventionalDiscovery()
                    .IncludeType<ReportWattHarborObservationCommandHandler>();
            });

            var app = builder.Build();
            app.UseHttpApiDefaults();
            app.UseObservationPayloadBuffering();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapObservations();
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("The test server has no address.");
            var address = addresses.Addresses.Single();
            return new ObservationHost(app, new HttpClient { BaseAddress = new Uri(address) }, stream);
        }

        public async Task<HttpResponseMessage> PostAsync(string body, string? apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/callbacks/wattharbor/observations")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (apiKey is not null)
            {
                request.Headers.Add(ObservationSourceAuthentication.ApiKeyHeader, apiKey);
            }

            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
