using EvCharging.Contracts.Charging;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class ChargingCommandTopology
{
    public const string StartQueue = "charging-start";

    public static WolverineOptions AddChargingCommandRouting(this WolverineOptions options)
    {
        // No listener is registered until charging execution is implemented.
        options.PublishMessage<StartCharging>().ToRabbitQueue(StartQueue, queue => queue.IsDurable = true);
        return options;
    }
}
