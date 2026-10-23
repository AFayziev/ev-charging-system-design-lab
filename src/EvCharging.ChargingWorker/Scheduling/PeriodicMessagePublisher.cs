using Wolverine;

namespace EvCharging.ChargingWorker.Scheduling;

/// <summary>
/// Sends one message at startup and then on every interval. The message handler finds the work that is due, so a
/// missed or duplicate tick changes nothing.
/// </summary>
internal sealed class PeriodicMessagePublisher<TMessage>(
    IServiceScopeFactory scopes,
    TimeSpan interval,
    TimeProvider clock,
    ILogger<PeriodicMessagePublisher<TMessage>> logger) : BackgroundService
    where TMessage : new()
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                stoppingToken.ThrowIfCancellationRequested();
                await bus.SendAsync(new TMessage());
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception, "Could not send {MessageType}; the next interval will retry.", typeof(TMessage).Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
