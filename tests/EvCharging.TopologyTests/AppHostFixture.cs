using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace EvCharging.TopologyTests;

public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? app;

    public DistributedApplication App => app ?? throw new InvalidOperationException("The application has not started.");

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EvCharging_AppHost>(
            ["--environment=Development"], timeout.Token);
        app = await builder.BuildAsync(timeout.Token);
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
        if (app is not null)
        {
            await app.DisposeAsync();
            app = null;
        }
    }
}
