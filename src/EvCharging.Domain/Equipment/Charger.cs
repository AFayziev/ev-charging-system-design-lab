namespace EvCharging.Domain.Equipment;

public sealed class Charger
{
    public Guid Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid StationId { get; set; }
    public required string Name { get; set; }

    public Station? Station { get; set; }
    public ProviderCharger? ProviderCharger { get; set; }

    public ICollection<Connector> Connectors { get; } = [];
}
