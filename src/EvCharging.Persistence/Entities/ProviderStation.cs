namespace EvCharging.Persistence.Entities;

public sealed class ProviderStation
{
    public long Id { get; set; }
    public long ChargingProviderId { get; set; }
    public long StationId { get; set; }
    public required string ExternalId { get; set; }
}
