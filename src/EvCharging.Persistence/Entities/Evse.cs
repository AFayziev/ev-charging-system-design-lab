namespace EvCharging.Persistence.Entities;

public sealed class Evse
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long ChargerId { get; set; }
    public int Number { get; set; }

    public Charger? Charger { get; set; }

    public ICollection<Connector> Connectors { get; } = [];
}
