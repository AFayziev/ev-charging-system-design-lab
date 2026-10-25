namespace EvCharging.SearchIndexWorker.Indexing;

/// <summary>The index could not be reached or refused the write for a reason that can clear.</summary>
internal sealed class SearchIndexUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
