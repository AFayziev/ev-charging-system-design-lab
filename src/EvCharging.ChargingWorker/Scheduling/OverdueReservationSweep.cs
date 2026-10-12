using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using Microsoft.Extensions.Options;
using Wolverine;

namespace EvCharging.ChargingWorker.Scheduling;

internal sealed partial class OverdueReservationSweep(
    IServiceScopeFactory scopes,
    IOptions<ReservationExpiryOptions> options,
    TimeProvider clock,
    ILogger<OverdueReservationSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                stoppingToken.ThrowIfCancellationRequested();
                await bus.SendAsync(new SweepOverdueReservations());
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogSweepSubmissionFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(LogLevel.Error, "Could not submit the overdue reservation sweep; the next interval will retry.")]
    private static partial void LogSweepSubmissionFailed(ILogger logger, Exception exception);
}
