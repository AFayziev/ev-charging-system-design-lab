using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed class WattHarborFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _charging;
    private HttpClient? _controls;
    private CallbackReceiver? _receiver;

    private DistributedApplication App => _app ?? throw new InvalidOperationException("The provider has not started.");
    public CallbackReceiver Receiver => _receiver ?? throw new InvalidOperationException("The receiver is not ready.");
    public HttpClient Charging => _charging ?? throw new InvalidOperationException("The charging client is not ready.");

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_Providers_AppHost>(
            ["--environment=Development"], timeout.Token);
        foreach (var container in builder.Resources.OfType<ContainerResource>())
        {
            var mounts = container.Annotations.OfType<ContainerMountAnnotation>()
                .Where(mount => mount.Type == ContainerMountType.Volume)
                .ToArray();
            foreach (var mount in mounts)
            {
                container.Annotations.Remove(mount);
                container.Annotations.Add(new ContainerMountAnnotation(
                    null, mount.Target, ContainerMountType.Volume, mount.IsReadOnly));
            }
        }

        // Short cadence and retry delays keep the observation scenarios fast.
        builder.CreateResourceBuilder<ProjectResource>("wattharbor-provider-api")
            .WithEnvironment("Telemetry__Interval", "00:00:00.300")
            .WithEnvironment("Telemetry__DeliveryPollInterval", "00:00:00.100")
            .WithEnvironment("Telemetry__RetryDelay", "00:00:00.100");
        _receiver = await CallbackReceiver.StartAsync();
        _app = await builder.BuildAsync(timeout.Token);
        try
        {
            await App.StartAsync(timeout.Token);
            await App.ResourceNotifications
                .WaitForResourceHealthyAsync("wattharbor-provider-api", timeout.Token);
            _charging = await CreateClientAsync("charging-worker", "charging-client-secret");
            _controls = await CreateClientAsync("simulation-control", "simulation-client-secret");
            using var target = await _controls.PutAsJsonAsync(
                "/simulation/callbacks/observations",
                new CallbackTarget(_receiver.Url.ToString(), CallbackReceiver.ApiKey),
                timeout.Token);
            target.EnsureSuccessStatusCode();
        }
        catch
        {
            await App.DisposeAsync();
            _app = null;
            throw;
        }

        async Task<HttpClient> CreateClientAsync(string clientId, string parameterName)
        {
            var secret = builder.Resources.OfType<ParameterResource>()
                .Single(parameter => parameter.Name == parameterName);
            using var identity = App.CreateHttpClient("provider-identity", "http");
            using var tokenResponse = await identity.PostAsync(
                "/realms/providers/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = await secret.GetValueAsync(timeout.Token)
                        ?? throw new InvalidOperationException("Client credentials are required.")
                }),
                timeout.Token);
            tokenResponse.EnsureSuccessStatusCode();
            var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(timeout.Token)
                ?? throw new InvalidOperationException("Missing access token.");
            var client = App.CreateHttpClient("wattharbor-provider-api", "http");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            return client;
        }
    }

    public async Task PrepareConnectorAsync(string stationId, string chargerId, string connectorId, bool enabled)
    {
        var controls = _controls ?? throw new InvalidOperationException("The control client is not ready.");
        using var response = await controls.PutAsJsonAsync(
            $"/simulation/stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}",
            new EquipmentSettings(22m, enabled));
        response.EnsureSuccessStatusCode();
    }

    public async Task ConfigureTelemetryFaultAsync(
        Guid externalSessionId, bool duplicate = false, bool deliverOutOfOrder = false, bool interrupt = false)
    {
        var controls = _controls ?? throw new InvalidOperationException("The control client is not ready.");
        using var response = await controls.PutAsJsonAsync(
            $"/simulation/sessions/{externalSessionId}/telemetry",
            new TelemetryFault(duplicate, deliverOutOfOrder, interrupt));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Restarts the provider process; its stored sessions and callback target remain.</summary>
    public async Task RestartProviderAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var commands = App.Services.GetRequiredService<ResourceCommandService>();
        var result = await commands.ExecuteCommandAsync(
            "wattharbor-provider-api", KnownResourceCommands.RestartCommand, timeout.Token);
        Assert.True(result.Success, result.Message);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("wattharbor-provider-api", timeout.Token);
    }

    public async Task DisposeAsync()
    {
        _charging?.Dispose();
        _controls?.Dispose();
        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private sealed record EquipmentSettings(decimal PowerKw, bool Enabled);
    private sealed record CallbackTarget(string Url, string ApiKey);
    private sealed record TelemetryFault(bool Duplicate, bool DeliverOutOfOrder, bool Interrupt);
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
