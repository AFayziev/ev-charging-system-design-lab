namespace EvCharging.Persistence.Entities;

public sealed class ProviderCharger
{
    public Guid Id { get; set; }
    public Guid ProviderStationId { get; set; }
    public Guid ChargerId { get; set; }
    public required string ExternalId { get; set; }
}
