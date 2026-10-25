using EvCharging.Messaging;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.SearchIndexWorker.Configuration;

internal sealed class SearchMessagingExtension(IOptions<SearchIndexingOptions> indexing) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.AddSearchFailurePolicies(indexing.Value.RetryDelays);

        // Events of one station form a group, so they are applied one at a time and in order. Group partitioning
        // orders within one node, so the queue is consumed by one node at a time; other instances stand by.
        options.ListenToRabbitQueue(StationEventTopology.SearchQueue)
            .ExclusiveNodeWithParallelism()
            .PartitionProcessingByGroupId(indexing.Value.IndexingSlots);
    }
}
