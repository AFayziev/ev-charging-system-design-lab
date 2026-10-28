using EvCharging.Contracts.Media;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class StationMediaTopology
{
    public const string ProcessingQueue = "station-media-processing";

    public static WolverineOptions AddStationMediaRouting(this WolverineOptions options)
    {
        options.PublishMessage<ProcessStationMediaMessage>()
            .ToRabbitQueue(ProcessingQueue, queue => queue.IsDurable = true);
        return options;
    }
}
