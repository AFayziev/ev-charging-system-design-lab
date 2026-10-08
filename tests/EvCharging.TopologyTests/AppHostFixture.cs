using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace EvCharging.TopologyTests;

public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The application has not started.");

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_AppHost>(
            ["--environment=Development"], timeout.Token);
        foreach (var resource in builder.Resources.OfType<ContainerResource>())
        {
            foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>()
                .Where(m => m.Type == ContainerMountType.Volume).ToArray())
            {
                resource.Annotations.Remove(mount);
                resource.Annotations.Add(new ContainerMountAnnotation(null, mount.Target, ContainerMountType.Volume, mount.IsReadOnly));
            }
        }

        _app = await builder.BuildAsync(timeout.Token);
        try
        {
            await App.StartAsync(timeout.Token);
            await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
            await App.ResourceNotifications.WaitForResourceHealthyAsync("frontend", timeout.Token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
