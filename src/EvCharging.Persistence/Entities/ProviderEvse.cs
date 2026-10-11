namespace EvCharging.Persistence.Entities;

public sealed class ProviderEvse
{
    public Guid Id { get; set; }
    public Guid ProviderChargerId { get; set; }
    public Guid EvseId { get; set; }
    public required string ExternalId { get; set; }
}
