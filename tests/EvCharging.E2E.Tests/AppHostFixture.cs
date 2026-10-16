using System.Globalization;
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
            "synchronous-charging-provider-api", cancellationToken);
        await _providers.ResourceNotifications.WaitForResourceHealthyAsync(
            "queued-charging-provider-api", cancellationToken);
        _providerClientSecret = await builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == "charging-client-secret")
            .GetValueAsync(cancellationToken);
    }

    private async Task StartPlatformAsync(CancellationToken cancellationToken)
    {
        var providers = _providers ?? throw new InvalidOperationException("The providers have not started.");
        var synchronousUrl = providers.GetEndpoint("synchronous-charging-provider-api", "http");
        var queuedUrl = providers.GetEndpoint("queued-charging-provider-api", "http");
        var tokenEndpoint = new Uri(
            providers.GetEndpoint("provider-identity", "http"),
            "/realms/providers/protocol/openid-connect/token");
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_AppHost>(
            ["--environment=Development", "--Parameters:api-instances=2",
                "--Parameters:gateway-instances=2",
                $"--Parameters:synchronous-provider-token-endpoint={tokenEndpoint}",
                $"--Parameters:synchronous-provider-url={synchronousUrl}",
                $"--Parameters:queued-provider-url={queuedUrl}"],
            cancellationToken);
        builder.Configuration["Parameters:synchronous-provider-client-secret"] = _providerClientSecret
            ?? throw new InvalidOperationException("The provider charging-client secret is required.");
        var api = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        var charging = builder.Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "charging-worker");
        var notifications = builder.Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "notification-worker");
        builder.CreateResourceBuilder(notifications)
            .WithEnvironment("NotificationPreparation__MaximumConcurrentPreparations", "4");
        builder.CreateResourceBuilder(charging)
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

        _app = await builder.BuildAsync(cancellationToken);
        await App.StartAsync(cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("frontend", cancellationToken);
    }

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
