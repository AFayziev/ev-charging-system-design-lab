using EvCharging.Contracts.Events.Reservations;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class ReservationEventTopology
{
    public const string Exchange = "reservation-events";
    public const string CreatedTopic = "reservation.created";
    public const string CancelledTopic = "reservation.cancelled";
    public const string ExpiredTopic = "reservation.expired";
    public const string ChargingQueue = "charging-reservation-events";
    public const string NotificationQueue = "notification-reservation-events";

    public static WolverineOptions AddReservationEventRouting(this WolverineOptions options)
    {
        options.ConfigureRabbitMq().DeclareExchange(
            Exchange,
            exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.BindTopic(CreatedTopic).ToQueue(ChargingQueue);
                exchange.BindTopic("reservation.*").ToQueue(NotificationQueue);
            });
        options.PublishMessagesToRabbitMqExchange<ReservationCreatedEvent>(Exchange, _ => CreatedTopic);
        options.PublishMessagesToRabbitMqExchange<ReservationCancelledEvent>(Exchange, _ => CancelledTopic);
        options.PublishMessagesToRabbitMqExchange<ReservationExpiredEvent>(Exchange, _ => ExpiredTopic);

        return options;
    }
}
