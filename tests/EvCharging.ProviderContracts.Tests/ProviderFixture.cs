using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EvCharging.ChargingWorker.Providers.Synchronous;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed class ProviderFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _charging;
    private HttpClient? _controls;
    private IHost? _adapterHost;

    private DistributedApplication App => _app ?? throw new InvalidOperationException("The provider has not started.");
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

        _app = await builder.BuildAsync(timeout.Token);
        try
        {
            await App.StartAsync(timeout.Token);
            await App.ResourceNotifications
                .WaitForResourceHealthyAsync("synchronous-charging-provider-api", timeout.Token);
            _charging = await CreateClientAsync("charging-worker", "charging-client-secret");
            _controls = await CreateClientAsync("simulation-control", "simulation-client-secret");
            var adapterBuilder = Host.CreateApplicationBuilder();
            adapterBuilder.AddServiceDefaults();
            var tokenEndpoint = new Uri(
                App.GetEndpoint("provider-identity", "http"),
                "/realms/providers/protocol/openid-connect/token");
            var secret = builder.Resources.OfType<ParameterResource>()
                .Single(parameter => parameter.Name == "charging-client-secret");
            adapterBuilder.Configuration["SynchronousChargingProvider:TokenEndpoint"] = tokenEndpoint.ToString();
            adapterBuilder.Configuration["SynchronousChargingProvider:ClientId"] = "charging-worker";
            adapterBuilder.Configuration["SynchronousChargingProvider:ClientSecret"] = await secret.GetValueAsync(
                timeout.Token);
            adapterBuilder.Configuration["SynchronousChargingProvider:AllowLocalHttp"] = "true";
            adapterBuilder.Configuration["SynchronousChargingProvider:RequestTimeout"] = "00:00:05";
            adapterBuilder.Configuration["services:synchronous-charging-provider-api:http:0"] =
                App.GetEndpoint("synchronous-charging-provider-api", "http").ToString();
            adapterBuilder.AddSynchronousChargingProvider();
            _adapterHost = adapterBuilder.Build();
            await _adapterHost.StartAsync(timeout.Token);
        }
        catch
        {
            _adapterHost?.Dispose();
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
            var client = App.CreateHttpClient("synchronous-charging-provider-api", "http");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            return client;
        }
    }

    internal SynchronousChargingProviderClient CreateAdapter() =>
        (_adapterHost ?? throw new InvalidOperationException("The adapter has not started."))
            .Services
            .GetRequiredService<SynchronousChargingProviderClient>();

    public async Task PrepareConnectorAsync(string stationId, string chargerId, string connectorId, bool enabled)
    {
        var controls = _controls ?? throw new InvalidOperationException("The control client is not ready.");
        using var response = await controls.PutAsJsonAsync(
            $"/simulation/stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}",
            new EquipmentSettings(22m, enabled));
        response.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (_adapterHost is not null)
        {
            await _adapterHost.StopAsync();
            _adapterHost.Dispose();
        }

        _charging?.Dispose();
        _controls?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private sealed record EquipmentSettings(decimal PowerKw, bool Enabled);
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
