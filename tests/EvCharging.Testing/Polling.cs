namespace EvCharging.Testing;

public static class Polling
{
    // Throws OperationCanceledException when the condition does not hold within the timeout.
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition, TimeSpan timeout, TimeSpan? interval = null)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!await condition())
        {
            await Task.Delay(interval ?? TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }
}
