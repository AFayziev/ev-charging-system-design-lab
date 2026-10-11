namespace EvCharging.Persistence.Entities;

public sealed class ProviderConnector
{
    public Guid Id { get; set; }
    public Guid ProviderEvseId { get; set; }
    public Guid ConnectorId { get; set; }
    public required string ExternalId { get; set; }
}
