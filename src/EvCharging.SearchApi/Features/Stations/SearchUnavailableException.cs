namespace EvCharging.SearchApi.Features.Stations;

/// <summary>The search index could not answer; callers see a typed 503 and may retry or fall back.</summary>
internal sealed class SearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
