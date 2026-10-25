namespace EvCharging.SearchIndexWorker.Tests;

// Delays each write by a random time, so concurrent handlers of one station overlap and could finish out of order.
internal sealed class DelayingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Put || request.Method == HttpMethod.Post)
        {
            await Task.Delay(Random.Shared.Next(0, 40), cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
