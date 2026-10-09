namespace EvCharging.Persistence.Entities;

public sealed class Connector
{
    public long Id { get; set; }
    public long EvseId { get; set; }
    public required string ConnectorType { get; set; }
    public required string PowerSupply { get; set; }
    public decimal RatedPowerKW { get; set; }
}
