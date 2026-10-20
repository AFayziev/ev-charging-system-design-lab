using EvCharging.Contracts.Charging;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class ChargingCommandTopology
{
    public const string StartQueue = "charging-start";
    public const string StopQueue = "charging-stop";
    public const string StatusCheckQueue = "charging-start-status-check";
    public const string StopStatusCheckQueue = "charging-stop-status-check";

    public static readonly IReadOnlyList<string> ListenerQueues =
        [StartQueue, StatusCheckQueue, StopQueue, StopStatusCheckQueue];

    public static WolverineOptions AddChargingCommandRouting(this WolverineOptions options)
    {
        options.PublishMessage<StartCharging>().ToRabbitQueue(StartQueue, queue => queue.IsDurable = true);
        options.PublishMessage<StopCharging>().ToRabbitQueue(StopQueue, queue => queue.IsDurable = true);
        options.PublishMessage<CheckChargingStart>().ToRabbitQueue(StatusCheckQueue, queue => queue.IsDurable = true);
        options.PublishMessage<CheckChargingStop>()
            .ToRabbitQueue(StopStatusCheckQueue, queue => queue.IsDurable = true);
        return options;
    }
}
