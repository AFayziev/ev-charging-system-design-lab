namespace EvCharging.Domain.Equipment;

public sealed class Evse
{
    public Guid Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid ChargerId { get; set; }
    public int Number { get; set; }

    public Charger? Charger { get; set; }

    public ICollection<Connector> Connectors { get; } = [];
}
