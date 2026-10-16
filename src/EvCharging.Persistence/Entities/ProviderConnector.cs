using EvCharging.Domain.Equipment;

namespace EvCharging.Persistence.Entities;

public sealed class ProviderConnector
{
    public Guid Id { get; set; }
    public Guid ProviderChargerId { get; set; }
    public Guid ConnectorId { get; set; }
    public string? ExternalEvseId { get; set; }
    public required string ExternalId { get; set; }
    public ProviderCharger? ProviderCharger { get; set; }
    public Connector? Connector { get; set; }
}
