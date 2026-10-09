namespace EvCharging.Persistence.Entities;

public sealed class ChargingProvider
{
    public long Id { get; set; }
    public required string Name { get; set; }
}
