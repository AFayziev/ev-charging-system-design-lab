using EvCharging.Contracts.Charging;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class ChargingCommandTopology
{
    public const string StartQueue = "charging-start";
    public const string StatusCheckQueue = "charging-start-status-check";

    public static WolverineOptions AddChargingCommandRouting(this WolverineOptions options)
    {
        options.PublishMessage<StartCharging>().ToRabbitQueue(StartQueue, queue => queue.IsDurable = true);
        options.PublishMessage<CheckChargingStart>().ToRabbitQueue(StatusCheckQueue, queue => queue.IsDurable = true);
        return options;
    }
}
