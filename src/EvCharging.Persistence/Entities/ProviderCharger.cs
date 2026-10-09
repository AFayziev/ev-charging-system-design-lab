namespace EvCharging.Persistence.Entities;

public sealed class ProviderCharger
{
    public long Id { get; set; }
    public long ProviderStationId { get; set; }
    public long ChargerId { get; set; }
    public required string ExternalId { get; set; }
}
