using EvCharging.Domain.Equipment;

namespace EvCharging.Persistence.Entities;

public sealed class ProviderStation
{
    public Guid Id { get; set; }
    public Guid ChargingProviderId { get; set; }
    public Guid StationId { get; set; }
    public required string ExternalId { get; set; }
    public ChargingProvider? ChargingProvider { get; set; }
    public Station? Station { get; set; }
}
