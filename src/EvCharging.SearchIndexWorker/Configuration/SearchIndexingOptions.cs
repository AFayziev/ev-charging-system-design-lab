using Wolverine.Configuration;

namespace EvCharging.SearchIndexWorker.Configuration;

internal sealed class SearchIndexingOptions
{
    public const string SectionName = "SearchIndexing";

    /// <summary>Stations indexed in parallel; one station's events are always applied in order.</summary>
    public PartitionSlots IndexingSlots { get; set; } = PartitionSlots.Five;

    /// <summary>Delays between retries of unavailable dependencies; the last delay repeats indefinitely.</summary>
    public TimeSpan[] RetryDelays { get; set; } = [];
}
