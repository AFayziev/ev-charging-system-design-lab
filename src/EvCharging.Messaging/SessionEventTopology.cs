using EvCharging.Contracts.Events.Sessions;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class SessionEventTopology
{
    public const string NotificationQueue = "notification-session-events";

    public static WolverineOptions AddSessionEventRouting(this WolverineOptions options)
    {
        options.PublishMessage<SessionCompletedEvent>()
            .ToRabbitQueue(NotificationQueue, queue => queue.IsDurable = true);
        return options;
    }
}
