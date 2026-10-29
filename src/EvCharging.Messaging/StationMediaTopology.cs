using EvCharging.Contracts.Media;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class StationMediaTopology
{
    public const string ProcessingQueue = "station-media-processing";
    public const string MaintenanceQueue = "station-media-maintenance";

    public static WolverineOptions AddStationMediaRouting(this WolverineOptions options)
    {
        options.PublishMessage<ProcessStationMediaMessage>()
            .ToRabbitQueue(ProcessingQueue, queue => queue.IsDurable = true);
        options.PublishMessage<CheckStationMediaUploadMessage>()
            .ToRabbitQueue(MaintenanceQueue, queue => queue.IsDurable = true);
        options.PublishMessage<DeleteStationMediaObjectsMessage>()
            .ToRabbitQueue(MaintenanceQueue, queue => queue.IsDurable = true);
        return options;
    }
}
