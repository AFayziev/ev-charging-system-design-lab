namespace EvCharging.Persistence.Entities;

public sealed class ChargingProvider
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}
