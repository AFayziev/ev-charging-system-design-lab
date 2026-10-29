using EvCharging.Messaging;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.MediaWorker.Configuration;

internal sealed class MediaMessagingExtension(IOptions<MediaProcessingOptions> processing) : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.AddMediaFailurePolicies(processing.Value.RetryDelays);

        // Each decoded photo holds its pixels in memory, so decoding is capped separately from housekeeping.
        options.ListenToRabbitQueue(StationMediaTopology.ProcessingQueue)
            .MaximumParallelMessages(processing.Value.MaximumConcurrentProcessing);
        options.ListenToRabbitQueue(StationMediaTopology.MaintenanceQueue);
    }
}
