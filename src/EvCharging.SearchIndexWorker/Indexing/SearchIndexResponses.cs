using Elastic.Transport.Products.Elasticsearch;

namespace EvCharging.SearchIndexWorker.Indexing;

internal static class SearchIndexResponses
{
    public static void ThrowIfFailed(ElasticsearchResponse response, string action, bool allowNotFound = false)
    {
        if (response.IsValidResponse || (allowNotFound && response.ApiCallDetails.HttpStatusCode == 404))
        {
            return;
        }

        Throw(
            response.ApiCallDetails.HttpStatusCode,
            $"Search index failed to {action}: {response.ApiCallDetails.DebugInformation}",
            response.ApiCallDetails.OriginalException);
    }

    // A bulk response is not "valid" when any item failed, so the items are judged one by one.
    public static void ThrowIfBulkRequestFailed(ElasticsearchResponse response, string action)
    {
        if (!response.ApiCallDetails.HasSuccessfulStatusCode)
        {
            ThrowIfFailed(response, action);
        }
    }

    public static bool IsWritten(int status) => status is >= 200 and <= 299;

    public static void Throw(int? status, string message, Exception? innerException = null)
    {
        // Only a malformed or oversized document can never succeed; blocks, throttling, and auth failures can clear.
        if (status is 400 or 413)
        {
            throw new InvalidOperationException(message, innerException);
        }

        throw new SearchIndexUnavailableException(message, innerException);
    }
}
