using EvCharging.Contracts.Events.Stations;
using Wolverine;

namespace EvCharging.Messaging;

public static class StationEventPublishingExtensions
{
    // One group per station lets the search worker apply a station's events strictly in order.
    public static ValueTask PublishStationChangedAsync(this IMessageBus bus, Guid stationId) =>
        bus.PublishAsync(new StationChangedEvent(stationId), new DeliveryOptions { GroupId = stationId.ToString() });
}
