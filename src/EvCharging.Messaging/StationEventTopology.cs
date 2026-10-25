using EvCharging.Contracts.Events.Stations;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EvCharging.Messaging;

public static class StationEventTopology
{
    private const string Exchange = "station-events";
    private const string ChangedTopic = "station.changed";
    public const string SearchQueue = "search-station-events";

    public static WolverineOptions AddStationEventRouting(this WolverineOptions options)
    {
        options.ConfigureRabbitMq().DeclareExchange(
            Exchange,
            exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.BindTopic(ChangedTopic).ToQueue(SearchQueue);
            });
        options.PublishMessagesToRabbitMqExchange<StationChangedEvent>(Exchange, _ => ChangedTopic);

        return options;
    }
}
