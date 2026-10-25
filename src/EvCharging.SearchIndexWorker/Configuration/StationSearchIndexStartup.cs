using EvCharging.SearchIndexWorker.Handlers;
using Wolverine;

namespace EvCharging.SearchIndexWorker.Configuration;

/// <summary>Asks for the index to be checked at start, so a reset or a mapping change is picked up.</summary>
internal sealed class StationSearchIndexStartup(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new EnsureStationSearchIndexCommand());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
