using EvCharging.Persistence.Auditing;

namespace EvCharging.Persistence.Entities;

public sealed class Station : IAuditableEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public uint Version { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public required string TimeZoneId { get; set; }
    public required string AccessInstructions { get; set; }

    public ICollection<Charger> Chargers { get; } = [];
}
