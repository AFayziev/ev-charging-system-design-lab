namespace EvCharging.Persistence.Entities;

public sealed class Charger
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public required string Name { get; set; }
}
