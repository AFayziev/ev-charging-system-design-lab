namespace EvCharging.Domain.Equipment;

public sealed class Connector
{
    public Guid Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid EvseId { get; set; }
    public int Number { get; set; }
    public required string ConnectorType { get; set; }
    public required string PowerSupply { get; set; }
    public decimal RatedPowerKW { get; set; }

    public Evse? Evse { get; set; }
}
