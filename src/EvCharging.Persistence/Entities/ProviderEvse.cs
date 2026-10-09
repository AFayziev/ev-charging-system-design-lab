namespace EvCharging.Persistence.Entities;

public sealed class ProviderEvse
{
    public long Id { get; set; }
    public long ProviderChargerId { get; set; }
    public long EvseId { get; set; }
    public required string ExternalId { get; set; }
}
