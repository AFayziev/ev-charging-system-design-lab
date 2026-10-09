namespace EvCharging.Persistence.Entities;

public sealed class ProviderConnector
{
    public long Id { get; set; }
    public long ProviderEvseId { get; set; }
    public long ConnectorId { get; set; }
    public required string ExternalId { get; set; }
}
