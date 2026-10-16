using EvCharging.Domain.Equipment;

namespace EvCharging.Persistence.Entities;

public sealed class ProviderCharger
{
    public Guid Id { get; set; }
    public Guid ProviderStationId { get; set; }
    public Guid ChargerId { get; set; }
    public required string ExternalId { get; set; }
    public ProviderStation? ProviderStation { get; set; }
    public Charger? Charger { get; set; }
}
