using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EvCharging.Domain.Reservations;
using EvCharging.Gateway.Authentication;
using EvCharging.Gateway.Persistence;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EvCharging.E2E.Tests;

public sealed class AppHostFixture : IAsyncLifetime
{
    public static ReservationBookingLimits BookingLimits { get; } = new(
        4, TimeSpan.FromHours(3), TimeSpan.FromDays(20), TimeSpan.FromMinutes(10));

    private DistributedApplication? _app;
    private DistributedApplication? _providers;
    private string? _providerClientSecret;
    private string? _simulationClientSecret;

    public DistributedApplication App =>
        _app ?? throw new InvalidOperationException("The application has not started.");

    public async Task<PlatformDbContext> CreatePlatformDbContextAsync() => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(await App.GetConnectionStringAsync("platform-direct"))
            .Options);

    public async Task<IHost> CreateAuthenticationHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:gateway-auth"] = await App.GetConnectionStringAsync("gateway-auth");
        builder.Configuration["AuthenticationDatabase:MaximumPoolSize"] = "1";
        builder.AddAuthenticationPersistence();
        builder.AddAuthenticationSessions();
        return builder.Build();
    }

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await StartProvidersAsync(timeout.Token);
            await StartPlatformAsync(timeout.Token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async Task StartProvidersAsync(CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_Providers_AppHost>(
            ["--environment=Development"], cancellationToken);
        UseEphemeralVolumes(builder);
        _providers = await builder.BuildAsync(cancellationToken);
        await _providers.StartAsync(cancellationToken);
        await _providers.ResourceNotifications.WaitForResourceHealthyAsync(
            "wattharbor-provider-api", cancellationToken);
        await _providers.ResourceNotifications.WaitForResourceHealthyAsync(
            "amptrail-provider-api", cancellationToken);
        _providerClientSecret = await builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == "charging-client-secret")
            .GetValueAsync(cancellationToken);
        _simulationClientSecret = await builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == "simulation-client-secret")
            .GetValueAsync(cancellationToken);
    }

    public async Task PrepareWattHarborConnectorAsync(
        string stationId, string chargerId, string connectorId, bool enabled)
    {
        await PutWattHarborControlAsync(
            $"/simulation/stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}",
            new ProviderEquipment(22m, enabled));
    }

    public async Task ConfigureWattHarborCommandFaultAsync(Guid operationId, Guid sessionId, int? lookupStatus)
    {
        await PutWattHarborControlAsync(
            $"/simulation/commands/{operationId}",
            new ProviderCommandFault(sessionId, true, lookupStatus));
    }

    /// <summary>Stops WattHarbor from sending observations for a session, or lets it send them again.</summary>
    public async Task ConfigureWattHarborTelemetryFaultAsync(Guid sessionId, bool interrupt)
    {
        await PutWattHarborControlAsync(
            $"/simulation/sessions/{sessionId}/telemetry",
            new ProviderTelemetryFault(false, false, interrupt));
    }

    private async Task PutWattHarborControlAsync(string path, object body)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var controls = await CreateWattHarborControlClientAsync(timeout.Token);
        using var response = await controls.PutAsJsonAsync(path, body, timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpClient> CreateWattHarborControlClientAsync(CancellationToken cancellationToken)
    {
        var providers = _providers ?? throw new InvalidOperationException("The providers have not started.");
        using var identity = providers.CreateHttpClient("provider-identity", "http");
        using var tokenResponse = await identity.PostAsync(
            "/realms/providers/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "simulation-control",
                ["client_secret"] = _simulationClientSecret
                    ?? throw new InvalidOperationException("Simulation credentials are required.")
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<ProviderToken>(cancellationToken)
            ?? throw new InvalidOperationException("Missing simulation token.");
        var controls = providers.CreateHttpClient("wattharbor-provider-api", "http");
        controls.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return controls;
    }

    private sealed record ProviderEquipment(decimal PowerKw, bool Enabled);
    private sealed record ProviderCallbackTarget(string Url, string ApiKey);
    private sealed record ProviderTelemetryFault(bool Duplicate, bool DeliverOutOfOrder, bool Interrupt);

    private sealed record ProviderCommandFault(Guid ExternalSessionId, bool LoseResponse, int? LookupStatusCode);
    private sealed record ProviderToken([property: JsonPropertyName("access_token")] string AccessToken);

    private async Task StartPlatformAsync(CancellationToken cancellationToken)
    {
        var providers = _providers ?? throw new InvalidOperationException("The providers have not started.");
        var wattHarborUrl = providers.GetEndpoint("wattharbor-provider-api", "http");
        var ampTrailUrl = providers.GetEndpoint("amptrail-provider-api", "http");
        var tokenEndpoint = new Uri(
            providers.GetEndpoint("provider-identity", "http"),
            "/realms/providers/protocol/openid-connect/token");
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_AppHost>(
            ["--environment=Development", "--Parameters:api-instances=2",
                "--Parameters:gateway-instances=2",
                "--ChargingProviders:WattHarborIntegrationKey=driver-wattharbor",
                "--ChargingProviders:AmpTrailIntegrationKey=driver-amptrail",
                $"--Parameters:wattharbor-provider-token-endpoint={tokenEndpoint}",
                $"--Parameters:wattharbor-provider-url={wattHarborUrl}",
                $"--Parameters:amptrail-provider-url={ampTrailUrl}"],
            cancellationToken);
        builder.Configuration["Parameters:wattharbor-provider-client-secret"] = _providerClientSecret
            ?? throw new InvalidOperationException("The provider charging-client secret is required.");
        var api = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        var charging = builder.Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "charging-worker");
        var notifications = builder.Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "notification-worker");
        builder.CreateResourceBuilder(notifications)
            .WithEnvironment("NotificationPreparation__MaximumConcurrentPreparations", "4");
        builder.CreateResourceBuilder(charging)
            .WithEnvironment("ChargingExecution__MaximumCommandRequests", "3")
            .WithEnvironment("ChargingExecution__MaximumConcurrentCommands", "2")
            .WithEnvironment("ChargingExecution__MaximumAutomaticStatusChecks", "2")
            .WithEnvironment("ChargingExecution__AutomaticStatusCheckInterval", "00:00:03")
            .WithEnvironment("ReservationExpiry__SweepInterval", "00:05:00")
            .WithEnvironment("ReservationExpiry__SweepBatchSize", "2")
            .WithEnvironment("ReservationExpiry__MaximumConcurrentChecks", "2");
        builder.CreateResourceBuilder(api)
            .WithEnvironment(
                "ReservationPolicy__MaximumOutstandingReservationsPerDriver",
                BookingLimits.MaximumOutstandingReservationsPerDriver.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(
                "ReservationPolicy__MaximumBookingDuration",
                BookingLimits.MaximumBookingDuration.ToString("c", CultureInfo.InvariantCulture))
            .WithEnvironment(
                "ReservationPolicy__MaximumAdvanceBookingHorizon",
                BookingLimits.MaximumAdvanceBookingHorizon.ToString("c", CultureInfo.InvariantCulture))
            .WithEnvironment(
                "ReservationPolicy__ArrivalAllowance",
                BookingLimits.ArrivalAllowance.ToString("c", CultureInfo.InvariantCulture));
        UseEphemeralVolumes(builder);

        var callbackKey = await builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == "wattharbor-callback-key")
            .GetValueAsync(cancellationToken) ?? throw new InvalidOperationException("The callback key is required.");

        _app = await builder.BuildAsync(cancellationToken);
        await App.StartAsync(cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("frontend", cancellationToken);
        await RegisterWattHarborCallbackAsync(
            new Uri(App.GetEndpoint("gateway", "http"), "/api/callbacks/wattharbor/observations"),
            callbackKey,
            cancellationToken);
    }

    // The provider is told where to deliver and with which key; the platform knows nothing of its internals.
    private async Task RegisterWattHarborCallbackAsync(Uri url, string apiKey, CancellationToken cancellationToken)
    {
        using var controls = await CreateWattHarborControlClientAsync(cancellationToken);
        using var response = await controls.PutAsJsonAsync(
            "/simulation/callbacks/observations",
            new ProviderCallbackTarget(url.ToString(), apiKey),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<JsonElement>> ReadObservationsAsync(
        Guid externalSessionId,
        Func<IReadOnlyList<JsonElement>, bool> condition,
        CancellationToken cancellationToken) =>
        await ObservationStreamReader.ReadUntilAsync(
            await App.GetConnectionStringAsync("kafka", cancellationToken)
                ?? throw new InvalidOperationException("The Kafka connection string is required."),
            "observations",
            externalSessionId,
            condition,
            cancellationToken);

    private static void UseEphemeralVolumes(IDistributedApplicationBuilder builder)
    {
        foreach (var resource in builder.Resources.OfType<ContainerResource>())
        {
            var volumeMounts = resource.Annotations.OfType<ContainerMountAnnotation>()
                .Where(m => m.Type == ContainerMountType.Volume)
                .ToArray();

            foreach (var mount in volumeMounts)
            {
                resource.Annotations.Remove(mount);
                resource.Annotations.Add(new ContainerMountAnnotation(
                    null, mount.Target, ContainerMountType.Volume, mount.IsReadOnly));
            }
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
                _app = null;
            }
        }
        finally
        {
            if (_providers is not null)
            {
                await _providers.DisposeAsync();
                _providers = null;
            }
        }
    }
}
