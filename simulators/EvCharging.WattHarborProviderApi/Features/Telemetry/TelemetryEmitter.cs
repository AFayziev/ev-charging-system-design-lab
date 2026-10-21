using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.Extensions.Options;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

/// <summary>Records one observation per active session on every interval.</summary>
internal sealed class TelemetryEmitter(
    IServiceScopeFactory scopes,
    IOptions<TelemetryOptions> options,
    TimeProvider clock,
    ILogger<TelemetryEmitter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EmitAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Recording session observations failed; the next interval retries.");
            }
        }
    }

    private async Task EmitAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var sessions = await database.LockActiveSessionsAsync(cancellationToken);
        // The time is taken under the locks, after any concurrent stop has finished.
        var now = clock.GetUtcNow();
        foreach (var session in sessions)
        {
            database.Observations.Add(ProviderObservation.Charging(session, now));
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
