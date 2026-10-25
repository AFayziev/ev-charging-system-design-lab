namespace EvCharging.SearchIndexWorker.Tests;

// Holds the alias swap of an index build until released, so a test can edit while the load is done but not yet swapped.
public sealed class IndexAliasSwapGate
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    public HttpMessageHandler Wrap(HttpMessageHandler inner) => new Handler(this, inner);

    private sealed class Handler(IndexAliasSwapGate gate, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/_aliases")
            {
                gate._reached.TrySetResult();
                await gate._released.Task.WaitAsync(cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
